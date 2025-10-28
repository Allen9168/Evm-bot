using Microsoft.Extensions.Logging;
using SoHa_Bot.Model;
using Soha.Model.Config;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    internal class TransactionService : IProject
    {
        private readonly ILogger<TransactionService> logger;
        private readonly AccountManager accountManager;
        private readonly ChainService chainService; // kept for DI symmetry

        private readonly List<AccountService> accountServices = new();

        // heartbeat
        private PeriodicTimer heartbeatTimer;
        private readonly CancellationTokenSource heartbeatCts = new();

        public TransactionService(
            ILogger<TransactionService> logger,
            AccountManager accountManager,
            TokenService _unusedTokenService, // kept for DI symmetry
            ChainService chainService)
        {
            this.logger = logger;
            this.accountManager = accountManager;
            this.chainService = chainService;
        }

        private ProjectConfig projectConfig;

        // trigger and target
        private string sender;
        private string targetContract;
        private string dataHex;

        // gas settings (config fallback)
        private bool autoGas = true;        // true: prefer following event fees
        private BigInteger gasPriceWeiCfg;  // legacy fallback from config (wei)
        private BigInteger gasLimit;        // Invoke.GasCount

        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled) return;
            this.projectConfig = projectConfig;

            // collect accounts
            string groupList = string.Empty;
            foreach (string accountGroup in projectConfig.AccountGroups)
            {
                var list = accountManager.GetAccount(accountGroup);
                accountServices.AddRange(list);
                groupList = string.IsNullOrEmpty(groupList) ? accountGroup : $"{groupList},{accountGroup}";
            }

            sender = (projectConfig.Sender ?? string.Empty).ToLowerInvariant();
            targetContract = (projectConfig.Contract ?? string.Empty).ToLowerInvariant();

            // read Invoke (top-level) or fallback to Burning.Invoke
            var invokeCfg = projectConfig.Invoke ?? projectConfig.Burning?.Invoke;
            if (invokeCfg == null)
            {
                logger.LogError("[TransactionService] 缺少调用部分 (Data/GasCount required, GasPrice optional).");
                return;
            }

            dataHex = NormalizeHex(invokeCfg.Data);
            if (string.IsNullOrWhiteSpace(dataHex))
            {
                logger.LogError("[TransactionService] 调用数据不能为空.");
                return;
            }

            // exact GasLimit from config; default to 210000 if missing
            gasLimit = new BigInteger(invokeCfg.GasCount ?? 210000);

            if (invokeCfg.GasPrice.HasValue)
            {
                autoGas = false; // prefer using fixed config price if provided
                gasPriceWeiCfg = new BigInteger(invokeCfg.GasPrice.Value) * 1_000_000_000; // gwei -> wei
            }

            logger.LogInformation("项目      : {Name}", projectConfig.Name);
            logger.LogInformation("已启动的地址组和数量 : {Groups}  Accounts: {Count}", groupList, accountServices.Count);
            logger.LogInformation("检测调用地址       : {Sender}", sender);
            logger.LogInformation("调用合约地址       : {Contract}", targetContract);
            logger.LogInformation("自定义调用Data         : {DataPreview}", dataHex.Length > 66 ? dataHex[..66] + "..." : dataHex);
            logger.LogInformation("Gas数量     : {GasLimit}  Gas价格: {GasPrice}",
                gasLimit,
                (autoGas ? "自动(跟随模式)" : $"{ToGwei(gasPriceWeiCfg):0.###} gwei"));

            // subscribe
            RobotUser.TransactionEvent += RobotUser_TransactionEventAsync;

            // heartbeat every 30s
            heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await heartbeatTimer.WaitForNextTickAsync(heartbeatCts.Token))
                    {
                        logger.LogInformation("[TransactionService] 该项目运行中: 开关={Enabled}, 在线地址数量={Accounts}, 检测到异动的地址={Sender}, 调用的合约={Target}",
                            this.projectConfig?.Enabled ?? false,
                            accountServices.Count,
                            sender,
                            targetContract);
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }, heartbeatCts.Token);

            await Task.CompletedTask;
        }

        // when the monitored sender appears in pending txs
        private async Task RobotUser_TransactionEventAsync(
            string tx,
            string from,
            string gasPriceHex,
            string maxFeePerGasHex,
            string maxPriorityFeePerGasHex,
            string valueHex,
            string inputHex)
        {
            try
            {
                if (string.IsNullOrEmpty(from) || !from.Equals(sender, StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.IsNullOrEmpty(targetContract) || string.IsNullOrEmpty(dataHex))
                    return;

                // parse fees from event
                var eventGasPrice = ParseWeiHex(gasPriceHex);
                var eventMaxFee = ParseWeiHex(maxFeePerGasHex);
                var eventTip = ParseWeiHex(maxPriorityFeePerGasHex);

                bool is1559 = (eventMaxFee > 0 && eventTip > 0);

                if (is1559)
                {
                    // follow 1559 exactly
                    logger.LogInformation(
                        "[TransactionService] 触发项目! -> 请求发送交易: srcTx={Tx} type=1559 fee=(maxFee={MaxFeeGwei:0.###} gwei, tip={TipGwei:0.###} gwei) gasLimit={GasLimit}, accounts={Accounts}, target={Target}",
                        tx, ToGwei(eventMaxFee), ToGwei(eventTip), gasLimit, accountServices.Count, targetContract);

                    var tasks = new List<Task<string>>(accountServices.Count);
                    foreach (var account in accountServices)
                    {
                        tasks.Add(account.Invoke1559Async(
                            targetContract,
                            gasLimit,
                            eventTip,
                            eventMaxFee,
                            dataHex));
                    }

                    var results = await Task.WhenAll(tasks);
                    for (int i = 0; i < results.Length; i++)
                    {
                        var h = results[i];
                        if (!string.IsNullOrEmpty(h))
                            logger.LogInformation("[TransactionService] 已使用你的私钥发送tx (1559): tx={TxHash}", h);
                        else
                            logger.LogWarning("[TransactionService] 调用（1559）返回空哈希 (account idx={Idx})", i);
                    }
                }
                else
                {
                    // use legacy gas price: prefer event gasPrice; fallback to config; fallback to 5 gwei
                    BigInteger gp = eventGasPrice;
                    if (gp <= 0) gp = autoGas ? gp : gasPriceWeiCfg;
                    if (gp <= 0) gp = new BigInteger(5_000_000_000L);

                    logger.LogInformation(
                        "[TransactionService] 触发项目! -> 请求发送交易(旧版): srcTx={Tx} type=legacy gasPrice={GasPriceGwei:0.###} gwei, gasLimit={GasLimit}, accounts={Accounts}, target={Target}",
                        tx, ToGwei(gp), gasLimit, accountServices.Count, targetContract);

                    var tasks = new List<Task<string>>(accountServices.Count);
                    foreach (var account in accountServices)
                    {
                        tasks.Add(account.InvokeAsync(targetContract, gp, gasLimit, dataHex));
                    }

                    var results = await Task.WhenAll(tasks);
                    for (int i = 0; i < results.Length; i++)
                    {
                        var h = results[i];
                        if (!string.IsNullOrEmpty(h))
                            logger.LogInformation("[TransactionService] 已使用你的私钥发送交易 (legacy): tx={TxHash}", h);
                        else
                            logger.LogWarning("[TransactionService] 发送返回空哈希 (account idx={Idx})", i);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[TransactionService] 调用失败");
            }
        }

        public Task UpdateAsync() => Task.CompletedTask;

        // --- helpers ---

        private static string NormalizeHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s;
        }

        private static BigInteger ParseWeiHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            var t = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (string.IsNullOrEmpty(t)) return 0;
            return BigInteger.Parse("0" + t, NumberStyles.HexNumber);
        }

        private static double ToGwei(BigInteger wei)
        {
            // convert via double to avoid decimal/BigInteger operator issues
            return (double)wei / 1e9d;
        }
    }
}
