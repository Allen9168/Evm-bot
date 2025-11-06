using Microsoft.Extensions.Logging;
using Soha.Model.Config;
using Soha.SwapFactory;
using Soha.Utils;
using SoHa_Bot.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    public sealed class BurningService : IProject
    {
        private readonly ILogger logger;
        private readonly AccountManager accountManager;
        private readonly TokenService tokenService;
        private readonly ChainService chainService;

        private readonly List<AccountService> accountServices = new();

        public BurningService(
            ILogger<BurningService> logger,
            AccountManager accountManager,
            TokenService tokenService,
            ChainService chainService)
        {
            this.logger = logger;
            this.accountManager = accountManager;
            this.tokenService = tokenService;
            this.chainService = chainService;
        }

        // ---- legacy gas (invoke / swap) ----
        private BigInteger invokeGasPriceWei;
        private BigInteger[] swapGasPriceWei;
        private BigInteger gasCountOrLimit; // legacy uses GasCount; 1559 uses GasLimit

        // ---- 1559 gas (invoke) ----
        private bool use1559Invoke;
        private BigInteger maxPriorityFeePerGasWei;
        private BigInteger maxFeePerGasWei;

        // ---- swap amounts (legacy only here) ----
        private BigInteger amountIn, amountOutMin;

        private string swapRoute, contract;
        private string[] buyPath, sellPath;
        private string data;

        private uint? startTimeUnix, startBlock; // config StartTime/StartBlock
        private ulong durationSeconds;

        private BurningConfigModel burningCfg;
        private ProjectConfig projectConfig;

        private ISwapFactory swapFactory;
        public bool ethPair;
        private string buyTokenContract;

        private Func<Task> burningMethod;

        // delay controls
        private int delayTimeMs;
        private int delayBlock; // reserved

        // optional approve/sell
        private bool approveEnabled;
        private bool autoSell;
        private BigInteger sellGasWei;

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

            logger.LogInformation("[{Name}] Starting BurningService", projectConfig.Name);
            logger.LogInformation("  AccountGroups: {Groups}  Accounts: {Count}", groupList, accountServices.Count);

            burningCfg = projectConfig.Burning ?? throw new InvalidOperationException("Burning config is required.");
            logger.LogInformation("  Mode: {Action}", burningCfg.Action);

            if (burningCfg.Action.Equals("swap", StringComparison.OrdinalIgnoreCase))
            {
                // SWAP path (legacy gas list)
                burningMethod = SwapTaskAsync;

                if (projectConfig.SwapRouter == null)
                    throw new InvalidOperationException("SwapRouter is required for swap mode.");

                swapRoute = projectConfig.SwapRouter.ToLowerInvariant();
                swapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

                if (burningCfg.Swap?.GasPrice == null || burningCfg.Swap.GasPrice.Count == 0)
                    throw new InvalidOperationException("Burning.Swap.GasPrice list is required.");

                swapGasPriceWei = new BigInteger[burningCfg.Swap.GasPrice.Count];
                for (int i = 0; i < swapGasPriceWei.Length; i++)
                {
                    // gwei -> wei (GasPrice list usually is integer gwei)
                    swapGasPriceWei[i] = ToWeiFromGwei(burningCfg.Swap.GasPrice[i]);
                }

                gasCountOrLimit = new BigInteger(burningCfg.Swap.GasCount ?? 210000);

                // amounts use token decimals helper (assumes tokenService returns base unit multiplier)
                var swap = burningCfg.Swap;
                var decIn = await tokenService.TokenDecimalsAsync(swap.Path[0]);
                var decOut = await tokenService.TokenDecimalsAsync(swap.Path[1]);
                amountIn = new BigInteger(swap.AmountIn * decIn);
                amountOutMin = new BigInteger(swap.AmountOutMin * decOut);

                buyPath = swap.Path;
                buyTokenContract = buyPath[^1];
                ethPair = buyPath[0].Equals(swapFactory.ETH, StringComparison.OrdinalIgnoreCase);

                logger.LogInformation("  Router: {Router}", swapRoute);
                logger.LogInformation("  Swap: amountIn={In} minOut={Out} path={Path}", swap.AmountIn, swap.AmountOutMin, string.Join(" -> ", buyPath));
                logger.LogInformation("  GasCount: {GasCount}  GasPrice list (gwei): {List}", gasCountOrLimit, string.Join(",", burningCfg.Swap.GasPrice));
            }
            else if (burningCfg.Action.Equals("invoke", StringComparison.OrdinalIgnoreCase))
            {
                // INVOKE path (legacy or 1559)
                burningMethod = InvokeTaskAsync;

                contract = projectConfig.Contract?.ToLowerInvariant() ?? throw new InvalidOperationException("Contract is required for invoke mode.");
                var inv = burningCfg.Invoke ?? throw new InvalidOperationException("Burning.Invoke is required for invoke mode.");

                use1559Invoke = inv.Use1559 ?? false;
                data = NormalizeHex(inv.Data);

                if (string.IsNullOrWhiteSpace(data))
                    throw new InvalidOperationException("Invoke.Data is required.");

                if (use1559Invoke)
                {
                    // EIP-1559 (decimal? in gwei)
                    gasCountOrLimit = new BigInteger(inv.GasLimit ?? 120000);
                    maxPriorityFeePerGasWei = ToWeiFromGwei(inv.MaxPriorityFeePerGas ?? 1.0m);
                    maxFeePerGasWei = ToWeiFromGwei(inv.MaxFeePerGas ?? 30.0m);

                    logger.LogInformation("  Invoke 1559:");
                    logger.LogInformation("    Contract: {Contract}", contract);
                    logger.LogInformation("    GasLimit: {Limit}", gasCountOrLimit);
                    logger.LogInformation("    MaxPriorityFeePerGas: {MP} gwei  MaxFeePerGas: {MF} gwei",
                        inv.MaxPriorityFeePerGas ?? 1.0m, inv.MaxFeePerGas ?? 30.0m);
                    logger.LogInformation("    Data: {Data}", data.Length > 66 ? data[..66] + "..." : data);
                }
                else
                {
                    // Legacy (ulong? in gwei)
                    var gpGwei = inv.GasPrice ?? 5UL;
                    invokeGasPriceWei = ToWeiFromGwei(gpGwei);
                    gasCountOrLimit = new BigInteger(inv.GasCount ?? 210000);

                    logger.LogInformation("  Invoke legacy:");
                    logger.LogInformation("    Contract: {Contract}", contract);
                    logger.LogInformation("    GasPrice: {GP} gwei  GasCount: {GC}", gpGwei, inv.GasCount ?? 210000);
                    logger.LogInformation("    Data: {Data}", data.Length > 66 ? data[..66] + "..." : data);
                }
            }
            else
            {
                throw new NotSupportedException("Burning.Action must be 'swap' or 'invoke'.");
            }

            // timing controls
            startTimeUnix = burningCfg.StartTime;
            startBlock = burningCfg.StartBlock;
            durationSeconds = burningCfg.Duration;

            if (burningCfg.Delay != null)
            {
                if (burningCfg.Delay.TimeDelay.HasValue) delayTimeMs = burningCfg.Delay.TimeDelay.Value;
                if (burningCfg.Delay.BlockDelay.HasValue) delayBlock = burningCfg.Delay.BlockDelay.Value;
            }

            // approve and autosell (optional)
            if (projectConfig.Approve != null)
            {
                approveEnabled = projectConfig.Approve.Enabled;
                logger.LogInformation("  [Approve] enabled={Enabled} spender={Spender} value={ValueHex}",
                    approveEnabled,
                    projectConfig.Approve.Spender ?? swapRoute,
                    projectConfig.Approve.Value ?? "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            }

            if (projectConfig.Sell != null)
            {
                autoSell = projectConfig.Sell.Enabled;
                if (autoSell)
                {
                    var sellList = new List<string>();
                    for (int i = buyPath.Length - 1; i >= 0; i--)
                        sellList.Add(buyPath[i]);

                    if (!string.IsNullOrEmpty(projectConfig.Sell.Receive))
                        sellList.Add(projectConfig.Sell.Receive);

                    sellPath = sellList.ToArray();

                    // gas for sell: use provided or first of swapGasPriceWei
                    if (projectConfig.Sell.GasPrice.HasValue)
                        sellGasWei = ToWeiFromGwei(projectConfig.Sell.GasPrice.Value);
                    else
                        sellGasWei = (swapGasPriceWei != null && swapGasPriceWei.Length > 0) ? swapGasPriceWei[0] : ToWeiFromGwei(5UL);

                    // pretty print gas gwei
                    decimal gasGweiForLog = projectConfig.Sell.GasPrice.HasValue
                        ? (decimal)projectConfig.Sell.GasPrice.Value
                        : ((swapGasPriceWei != null && swapGasPriceWei.Length > 0) ? WeiToGwei(swapGasPriceWei[0]) : 5m);

                    logger.LogInformation("  [AutoSell] enabled=True");
                    logger.LogInformation("    Path: {Path}", string.Join(" -> ", sellPath));
                    logger.LogInformation("    Delay: time={TimeDelay}ms block={BlockDelay}",
                        projectConfig.Sell.Delay?.TimeDelay ?? 0, projectConfig.Sell.Delay?.BlockDelay ?? 0);
                    logger.LogInformation("    Percentage: {Pct}%   MinOut: {MinOut}   GasPrice: {GasGwei} gwei",
                        projectConfig.Sell.Percentage ?? 100,
                        projectConfig.Sell.AmountOutMin ?? 0,
                        gasGweiForLog);
                }
            }

            logger.LogWarning("!!! Please double-check the above parameters !!!");
        }

        public async Task UpdateAsync()
        {
            double startAt = 0, endAt = 0;

            // main timed loop
            await Task.Run(async () =>
            {
                while (projectConfig.Enabled)
                {
                    ulong blockNumber = chainService.BlockNumber;
                    double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    if (startAt > 0)
                    {
                        await burningMethod.Invoke();

                        if (now > endAt)
                        {
                            startAt = 0;
                            logger.LogInformation("[{Name}] Burning window finished.", projectConfig.Name);
                            break;
                        }

                        if (delayTimeMs > 0)
                            Thread.Sleep(delayTimeMs);

                        continue;
                    }

                    if (startBlock.HasValue)
                    {
                        if (blockNumber > startBlock.Value - 1)
                        {
                            startAt = now;
                            endAt = startAt + durationSeconds;
                            continue;
                        }
                        else
                        {
                            ulong blocksToWait = startBlock.Value - blockNumber;
                            logger.LogInformation("[{Name}] Waiting for blocks: {Blocks}", projectConfig.Name, blocksToWait);
                            Thread.Sleep((int)(blocksToWait * 800)); // rough estimate
                        }
                    }

                    if (startTimeUnix.HasValue)
                    {
                        if (now >= startTimeUnix.Value)
                        {
                            startAt = now;
                            endAt = startAt + durationSeconds;
                            continue;
                        }
                        else
                        {
                            double sec = startTimeUnix.Value - now;
                            logger.LogInformation("[{Name}] Waiting for time: {Seconds}s", projectConfig.Name, sec.ToString("F0"));
                            Thread.Sleep((int)(sec * 800)); // rough estimate
                        }
                    }
                }
            });

            // optional approve/sell loop
            await Task.Run(async () =>
            {
                while (autoSell || approveEnabled)
                {
                    foreach (var account in accountServices)
                    {
                        BigInteger balance = BigInteger.Zero;

                        try
                        {
                            if (!string.IsNullOrEmpty(buyTokenContract))
                                balance = await account.BalanceOfAsync(buyTokenContract);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[BurningService] BalanceOfAsync failed (autoSell/approve loop).");
                        }

                        if (balance > 0)
                        {
                            Stop();

                            if (approveEnabled && !account.Approves.TryGetValue(buyTokenContract, out _))
                            {
                                approveEnabled = false;
                                BigInteger approveValue = ParseHexToBigInteger(projectConfig?.Approve?.Value)
                                    ?? BigInteger.Parse("0ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber);

                                try
                                {
                                    await account.ApproveAsync(
                                        buyTokenContract,
                                        projectConfig.Approve?.Spender ?? swapRoute,
                                        approveValue);

                                    logger.LogInformation("[Approve] Sent for token={Token}", buyTokenContract);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "[BurningService] ApproveAsync failed.");
                                }
                            }

                            if (autoSell)
                            {
                                try
                                {
                                    // percentage
                                    var pct = projectConfig.Sell?.Percentage ?? 100;
                                    if (pct <= 0) pct = 100;
                                    balance = balance * pct / 100;

                                    ulong? sellBlockAt = null, sellTimeMs = null;
                                    if (projectConfig.Sell?.Delay?.BlockDelay.HasValue == true)
                                    {
                                        var cur = chainService.BlockNumber;
                                        sellBlockAt = cur + (ulong)projectConfig.Sell.Delay.BlockDelay.Value;
                                    }
                                    if (projectConfig.Sell?.Delay?.TimeDelay.HasValue == true)
                                    {
                                        sellTimeMs = (ulong)(projectConfig.Sell.Delay.TimeDelay.Value);
                                    }

                                    await account.SellTokensForTokensAsync(
                                        swapFactory,
                                        balance,
                                        new BigInteger(projectConfig.Sell?.AmountOutMin ?? 0),
                                        sellPath,
                                        sellGasWei,
                                        gasCountOrLimit,
                                        sellBlockAt,
                                        sellTimeMs);

                                    logger.LogInformation("[AutoSell] Sent.");
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "[BurningService] AutoSell failed.");
                                }
                            }
                        }
                    }

                    // avoid tight loop
                    await Task.Delay(1000);
                }
            });
        }

        private async Task InvokeTaskAsync()
        {
            var tasks = new List<Task<string>>(accountServices.Count);

            foreach (var account in accountServices)
            {
                try
                {
                    if (use1559Invoke)
                    {
                        tasks.Add(account.Invoke1559Async(
                            contract,
                            gasLimit: gasCountOrLimit,
                            maxPriorityFeePerGas: maxPriorityFeePerGasWei,
                            maxFeePerGas: maxFeePerGasWei,
                            data: data));
                    }
                    else
                    {
                        tasks.Add(account.InvokeAsync(
                            contract,
                            gasPrice: invokeGasPriceWei,
                            gasCount: gasCountOrLimit,
                            Data: data));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Invoke] Enqueue failed (account).");
                }
            }

            foreach (var bucket in TaskHelp.Interleaved(tasks))
            {
                try
                {
                    var t = await bucket;
                    var hash = await t;
                    logger.LogInformation("[Invoke] Tx: {Hash}", hash);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Invoke] Send failed.");
                }
            }
        }

        private async Task SwapTaskAsync()
        {
            var tasks = new List<Task<string>>();

            foreach (var account in accountServices)
            {
                try
                {
                    if (ethPair)
                    {
                        // ETH -> token (legacy only here)
                        for (int i = 0; i < swapGasPriceWei.Length; i++)
                        {
                            tasks.Add(account.ExactETHForTokensAsync(
                                swapFactory, amountIn, amountOutMin, buyPath,
                                gasPrice: swapGasPriceWei[i],
                                gasCount: gasCountOrLimit));
                        }
                    }
                    else
                    {
                        // token -> token (legacy only here)
                        for (int i = 0; i < swapGasPriceWei.Length; i++)
                        {
                            tasks.Add(account.ExactTokensForTokensAsync(
                                swapFactory, amountIn, amountOutMin, buyPath,
                                gasPrice: swapGasPriceWei[i],
                                gasCount: gasCountOrLimit));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Swap] Enqueue failed (account).");
                }
            }

            foreach (var bucket in TaskHelp.Interleaved(tasks))
            {
                try
                {
                    var t = await bucket;
                    var hash = await t;
                    logger.LogInformation("[Buy] Tx: {Hash}", hash);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Swap] Send failed.");
                }
            }
        }

        private void Stop() => projectConfig.Enabled = false;

        // ------------- helpers -------------

        // gwei (decimal) -> wei
        private static BigInteger ToWeiFromGwei(decimal gwei)
        {
            return new BigInteger(gwei * 1_000_000_000m);
        }

        // gwei (ulong) -> wei
        private static BigInteger ToWeiFromGwei(ulong gwei)
        {
            return new BigInteger((decimal)gwei * 1_000_000_000m);
        }

        // wei -> gwei (decimal for logging)
        private static decimal WeiToGwei(BigInteger wei)
        {
            return (decimal)wei / 1_000_000_000m;
        }

        private static string NormalizeHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s;
        }

        private static BigInteger? ParseHexToBigInteger(string hexOrNull)
        {
            if (string.IsNullOrWhiteSpace(hexOrNull)) return null;
            var t = hexOrNull.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hexOrNull[2..] : hexOrNull;
            if (string.IsNullOrEmpty(t)) return null;
            return BigInteger.Parse("0" + t, NumberStyles.HexNumber);
        }
    }
}
