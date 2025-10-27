using Microsoft.Extensions.Logging;
using SoHa_Bot.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    internal class TransactionService : IProject
    {
        private readonly ILogger<TransactionService> logger;
        private readonly AccountManager accountManager;
        private readonly ChainService chainService; // 如未使用可移除

        private readonly List<AccountService> accountServices = new();

        public TransactionService(
            ILogger<TransactionService> logger,
            AccountManager accountManager,
            TokenService _unusedTokenService, // 不再需要，可从构造函数和 DI 中移除
            ChainService chainService)
        {
            this.logger = logger;
            this.accountManager = accountManager;
            this.chainService = chainService;
        }

        private ProjectConfig projectConfig;

        // 监听触发者 & 目标合约 & 自定义 data
        private string sender;
        private string targetContract;
        private string dataHex;

        // Gas 设置
        private bool autoGas = true;        // true=跟踪事件里的 gas；false=用固定 gasPrice
        private BigInteger gasPriceWei;     // 固定 gasPrice（wei）
        private BigInteger gasLimit;        // GasCount

        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled) return;
            this.projectConfig = projectConfig;

            // 账号组聚合
            string groupList = string.Empty;
            foreach (string accountGroup in projectConfig.AccountGroups)
            {
                var list = accountManager.GetAccount(accountGroup);
                accountServices.AddRange(list);
                groupList = string.IsNullOrEmpty(groupList) ? accountGroup : $"{groupList},{accountGroup}";
            }

            sender = projectConfig.Sender?.ToLower();
            targetContract = projectConfig.Contract?.ToLower();

            // 读取顶层 Invoke；也可兼容 Burning.Invoke（按需保留）
            var invokeCfg = projectConfig.Invoke ?? projectConfig.Burning?.Invoke;
            if (invokeCfg == null)
            {
                logger.LogError($"[{projectConfig.Name}] 缺少 Invoke 配置（Data/GasCount 必填，GasPrice 可选）。");
                return;
            }

            dataHex = NormalizeHex(invokeCfg.Data);
            if (string.IsNullOrWhiteSpace(dataHex))
            {
                logger.LogError($"[{projectConfig.Name}] Invoke.Data 不能为空。");
                return;
            }

            gasLimit = new BigInteger(invokeCfg.GasCount ?? 210000);
            if (invokeCfg.GasPrice.HasValue)
            {
                autoGas = false;
                gasPriceWei = new BigInteger(invokeCfg.GasPrice.Value * 1_000_000_000L); // gwei -> wei
            }

            logger.LogInformation($" 项目       : {projectConfig.Name}");
            logger.LogInformation($" 账号组     : {groupList} 账号数量 : {accountServices.Count}");
            logger.LogInformation($" 触发者     : {sender}");
            logger.LogInformation($" 目标合约   : {targetContract}");
            logger.LogInformation($" Data       : {(dataHex.Length > 66 ? dataHex[..66] + "..." : dataHex)}");
            logger.LogInformation($" GasLimit   : {gasLimit}  GasPrice : {(autoGas ? "Auto(跟踪)" : $"{gasPriceWei / 1_000_000_000} gwei")}");

            // 订阅链上事件
            RobotUser.TransactionEvent += RobotUser_TransactionEventAsync;
        }

        // 命中 sender 后，直接向 targetContract 发送自定义 data（不再做任何 Swap）
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
                if (!from.Equals(sender, StringComparison.OrdinalIgnoreCase)) return;
                if (string.IsNullOrEmpty(targetContract) || string.IsNullOrEmpty(dataHex)) return;

                // 选择 gas：固定优先，否则跟踪事件（BSC 常用 legacy gasPrice；EIP-1559 则取 maxFeePerGas）
                BigInteger gp = autoGas ? PickGasFromEvent(gasPriceHex, maxFeePerGasHex) : gasPriceWei;
                if (gp <= 0) gp = new BigInteger(5_000_000_000L); // 兜底 5 gwei，可按需调整

                var tasks = new List<Task<string>>();
                foreach (var account in accountServices)
                {
                    tasks.Add(account.InvokeAsync(targetContract, gp, gasLimit, dataHex));
                }

                var hashes = await Task.WhenAll(tasks);
                foreach (var h in hashes)
                {
                    logger.LogInformation($"[Invoke] Tx: {h}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Invoke] 发送自定义 data 失败");
            }
        }

        public Task UpdateAsync() => Task.CompletedTask;

        // Helpers
        private static string NormalizeHex(string s) =>
            string.IsNullOrWhiteSpace(s) ? null : (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s);

        private static BigInteger ParseHexWei(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            var t = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (string.IsNullOrEmpty(t)) return 0;
            return BigInteger.Parse("0" + t, NumberStyles.HexNumber);
        }

        private static BigInteger PickGasFromEvent(string gasPriceHex, string maxFeePerGasHex)
        {
            var legacy = ParseHexWei(gasPriceHex);
            if (legacy > 0) return legacy;
            var maxFee = ParseHexWei(maxFeePerGasHex);
            if (maxFee > 0) return maxFee;
            return 0;
        }
    }
}
