using Microsoft.Extensions.Logging;
using Soha.SwapFactory;
using Soha.Utils;
using SoHa_Bot.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    public class SwapService(
        ILogger<SwapService> logger,
        AccountManager accountManager,
        TokenService tokenService,
        ChainService chainService) : IProject
    {
        private readonly ILogger logger = logger;
        private readonly TokenService tokenService = tokenService;
        private readonly ChainService chainService = chainService;
        private readonly AccountManager accountManager = accountManager;

        private readonly List<AccountService> accountServices = [];
        private ISwapFactory SwapFactory;
        private ProjectConfig projectConfig;

        private string swapRoute = string.Empty;
        private string contract = string.Empty;
        private string send = string.Empty;

        private int repeat;
        private int outTime;
        private decimal LiquidityETHAmount;
        private bool LiquidityEnabled, LiquidityETHEnabled;

        #region Exact
        private BigInteger gasPrice, gasCount;
        private BigInteger amountIn, amountOutMin;
        private bool IsSlippage;
        #endregion

        #region Delay
        private bool IsdelayTime, IsdelayBlock;
        private int delayTime, delayBlock;
        #endregion

        private bool AutoGas = true;
        private bool Approve;
        private bool AutoSell;
        private BigInteger sellGas;

        private bool TransferETHPair;

        public Dictionary<string, decimal> Liquidity1 { get; set; }

        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled) return;

            this.projectConfig = projectConfig;

            swapRoute = projectConfig.SwapRouter.ToLowerInvariant();
            contract = projectConfig.Contract.ToLowerInvariant();
            logger.LogInformation("Swap route: {SwapRoute} | Contract: {Contract}", swapRoute, contract);

            SwapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

            // collect accounts
            var groupList = string.Empty;
            foreach (var accountGroup in projectConfig.AccountGroups)
            {
                var list = accountManager.GetAccount(accountGroup);
                accountServices.AddRange(list);
                groupList = string.IsNullOrEmpty(groupList) ? accountGroup : $"{groupList},{accountGroup}";
            }

            if (projectConfig.Exact != null)
            {
                // determine source token
                if (string.IsNullOrEmpty(projectConfig.Exact.Send))
                {
                    send = SwapFactory.ETH;
                    TransferETHPair = true;
                }
                else
                {
                    send = projectConfig.Exact.Send;
                    if (send.Equals(SwapFactory.ETH, StringComparison.OrdinalIgnoreCase))
                        TransferETHPair = true;
                }

                decimal unit = await tokenService.TokenDecimalsAsync(send);

                IsSlippage = projectConfig.Exact.AmountOutMin.HasValue;
                if (IsSlippage)
                {
                    amountOutMin = new BigInteger(projectConfig.Exact.AmountOutMin!.Value * unit);
                }

                if (projectConfig.Exact.GasPrice.HasValue)
                {
                    AutoGas = false;
                    gasPrice = new BigInteger(projectConfig.Exact.GasPrice!.Value * 1_000_000_000m);
                }

                gasCount = new BigInteger(projectConfig.Exact.GasCount!.Value);

                var amountOutMinStr = amountOutMin > 0 ? amountOutMin.ToString() : "null";
                logger.LogInformation(
                    "amountOutMin={AmountOutMin} | gasPrice={GasPriceDesc}",
                    amountOutMinStr,
                    AutoGas ? "follow" : $"{WeiToGwei(gasPrice):0.####} gwei");

                amountIn = new BigInteger(projectConfig.Transaction.Cost * unit);
                repeat = projectConfig.Transaction.Count;
                outTime = projectConfig.Transaction.OutTime;

                logger.LogInformation("amountIn={AmountIn} | repeat={Repeat} | outTime={OutTime}", projectConfig.Transaction.Cost, repeat, outTime);
                logger.LogInformation("groups={Groups} | accounts={Count}", groupList, accountServices.Count);
            }
            else
            {
                logger.LogError("Project {ProjectName}: Exact config must not be null.", projectConfig.Name);
                return;
            }

            if (projectConfig.Delay != null)
            {
                IsdelayTime = projectConfig.Delay.TimeDelay.HasValue;
                if (IsdelayTime)
                {
                    // config expressed as seconds → convert to ms for Thread.Sleep
                    delayTime = projectConfig.Delay.TimeDelay!.Value * 1000;
                }

                IsdelayBlock = projectConfig.Delay.BlockDelay.HasValue;
                if (IsdelayBlock)
                {
                    delayBlock = projectConfig.Delay.BlockDelay!.Value;
                }

                logger.LogInformation("delayTime(ms)={DelayTime} | delayBlock={DelayBlock}", delayTime, delayBlock);
            }

            if (projectConfig.Liquiditys.Liquidity != null)
            {
                LiquidityEnabled = projectConfig.Liquiditys.Liquidity.Enabled;
                if (LiquidityEnabled)
                {
                    Liquidity1 = new(StringComparer.OrdinalIgnoreCase);
                    SwapFactory.AddLiquidityEvent += SwapFactory_AddLiquidityEventAsync;
                    logger.LogInformation("[Liquidity] enabled=True");

                    foreach (string token in projectConfig.Liquiditys.Liquidity.Token)
                    {
                        var dec = await tokenService.TokenDecimalsAsync(token);
                        Liquidity1[token] = projectConfig.Liquiditys.Liquidity.Amount * dec;
                        logger.LogInformation("  token={Token} | threshold={Amount}", token, projectConfig.Liquiditys.Liquidity.Amount);
                    }
                }
            }

            if (projectConfig.Liquiditys.LiquidityETH != null)
            {
                LiquidityETHEnabled = projectConfig.Liquiditys.LiquidityETH.Enabled;
                if (LiquidityETHEnabled)
                {
                    var dec = await tokenService.TokenDecimalsAsync(SwapFactory.ETH);
                    LiquidityETHAmount = projectConfig.Liquiditys.LiquidityETH.Amount * dec;
                    SwapFactory.AddLiquidityETHEvent += SwapFactory_AddLiquidityETHEventAsync;

                    logger.LogInformation("[LiquidityETH] enabled=True");
                    logger.LogInformation("  token={Token} | threshold={Amount}", SwapFactory.ETH, projectConfig.Liquiditys.LiquidityETH.Amount);
                }
            }

            if (projectConfig.Approve != null)
            {
                Approve = projectConfig.Approve.Enabled;
                logger.LogInformation("[Approve] enabled={Enabled}", Approve);
                logger.LogInformation("  spender={Spender} | value={ValueHex}", projectConfig.Approve?.Spender, projectConfig.Approve?.Value);
            }

            if (projectConfig.Sell != null)
            {
                AutoSell = projectConfig.Sell.Enabled;
                logger.LogInformation("[AutoSell] enabled={Enabled}", AutoSell);

                var td = projectConfig.Sell.Delay?.TimeDelay ?? 0;
                var bd = projectConfig.Sell.Delay?.BlockDelay ?? 0;
                var pct = projectConfig.Sell.Percentage ?? 100;

                logger.LogInformation("  timeDelay={TimeDelay}s | blockDelay={BlockDelay}", td, bd);
                logger.LogInformation("  percentage={Percentage}% | minOut={MinOut}", pct, projectConfig.Sell?.AmountOutMin);
                logger.LogInformation("  gasPrice={GasPrice}", projectConfig.Sell?.GasPrice?.ToString() ?? "follow");
            }
        }

        public Task StopAsync()
        {
            if (LiquidityEnabled)
            {
                SwapFactory.AddLiquidityEvent -= SwapFactory_AddLiquidityEventAsync;
            }
            if (LiquidityETHEnabled)
            {
                SwapFactory.AddLiquidityETHEvent -= SwapFactory_AddLiquidityETHEventAsync;
            }
            return Task.CompletedTask;
        }

        private async Task SwapFactory_AddLiquidityETHEventAsync(
            string tx,
            string from,
            BigInteger gasPrice,
            BigInteger? maxFeePerGas,
            BigInteger? maxPriorityFeePerGas,
            decimal value,
            string token,
            decimal amountTokenDesired)
        {
            if (token.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                if (value >= LiquidityETHAmount)
                {
                    await TokenTransferAsync(gasPrice, maxFeePerGas, maxPriorityFeePerGas, SwapFactory.ETH, token);
                }
                else
                {
                    logger.LogError("[LiquidityETH] insufficient pool size. got={Got} need={Need}", value, LiquidityETHAmount);
                }
                return;
            }

            await Task.Run(async () =>
            {
                try
                {
                    var ethNameTask = tokenService.TokenNameAsync(SwapFactory.ETH);
                    var ethDecimalTask = tokenService.TokenDecimalsAsync(SwapFactory.ETH);
                    var tokenNameTask = tokenService.TokenNameAsync(token);
                    var tokenDecimalTask = tokenService.TokenDecimalsAsync(token);
                    await Task.WhenAll(ethNameTask, ethDecimalTask, tokenNameTask, tokenDecimalTask);

                    logger.LogInformation(
                        "[LiquidityETH] tx={Tx} | from={From} | gasPrice={GasGwei} gwei | value={EthValue} {EthSym} | token={TokenSym} | amountTokenDesired={Amount}",
                        tx,
                        from,
                        WeiToGwei(gasPrice),
                        value / await ethDecimalTask,
                        await ethNameTask,
                        await tokenNameTask,
                        amountTokenDesired / await tokenDecimalTask);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[LiquidityETH] log compose failed.");
                }
            });
        }

        private async Task SwapFactory_AddLiquidityEventAsync(
            string tx,
            string from,
            BigInteger gasPrice,
            BigInteger? maxFeePerGas,
            BigInteger? maxPriorityFeePerGas,
            string tokenA,
            string tokenB,
            decimal amountADesired,
            decimal amountBDesired)
        {
            string buyToken = string.Empty, routeToken = string.Empty;
            decimal amount = 0, balance = 0;
            bool doBuy = false;

            if (tokenA.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                amount = amountADesired;
                balance = amountBDesired;
                routeToken = tokenB;
                buyToken = tokenA;
                doBuy = true;
            }
            else if (tokenB.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                balance = amountADesired;
                amount = amountBDesired;
                routeToken = tokenA;
                buyToken = tokenB;
                doBuy = true;
            }

            if (doBuy)
            {
                if (Liquidity1 != null && Liquidity1.TryGetValue(routeToken, out decimal threshold))
                {
                    if (balance >= threshold)
                    {
                        await TokenTransferAsync(gasPrice, maxFeePerGas, maxPriorityFeePerGas, routeToken, buyToken);
                    }
                    else
                    {
                        logger.LogError("[Liquidity] balance={Balance} < threshold={Threshold}", balance, threshold);
                    }
                }
                else
                {
                    logger.LogError("[Liquidity] unknown route token in add-liquidity: {RouteToken}", routeToken);
                }
            }

            await Task.Run(async () =>
            {
                try
                {
                    var nameATask = tokenService.TokenNameAsync(tokenA);
                    var nameBTask = tokenService.TokenNameAsync(tokenB);
                    var decATask = tokenService.TokenDecimalsAsync(tokenA);
                    var decBTask = tokenService.TokenDecimalsAsync(tokenB);
                    await Task.WhenAll(nameATask, nameBTask, decATask, decBTask);

                    logger.LogInformation(
                        "[Liquidity] tx={Tx} | from={From} | gasPrice={GasGwei} gwei | tokenA={TokenA} | tokenB={TokenB} | amountADesired={AmtA} | amountBDesired={AmtB}",
                        tx,
                        from,
                        WeiToGwei(gasPrice),
                        await nameATask,
                        await nameBTask,
                        amountADesired / await decATask,
                        amountBDesired / await decBTask);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Liquidity] log compose failed.");
                }
            });
        }

        private async Task TokenTransferAsync(
            BigInteger gasPrice,
            BigInteger? maxFeePerGas,
            BigInteger? maxPriorityFeePerGas,
            string routeToken,
            string buyToken)
        {
            BeforeTokenTransfer();

            if (AutoGas)
            {
                this.gasPrice = gasPrice;
            }

            var buyTasks = new List<Task<List<Task<string>>>>();
            string[] buyPath;
            var sellPath = new List<string>();

            if (send.Equals(routeToken, StringComparison.OrdinalIgnoreCase))
            {
                buyPath = [routeToken, buyToken];
                sellPath.AddRange([buyToken, routeToken]);
            }
            else
            {
                buyPath = [send, routeToken, buyToken];
                sellPath.AddRange([buyToken, routeToken, send]);
            }

            foreach (var accountService in accountServices)
            {
                Task<List<Task<string>>> buyTask;
                if (TransferETHPair)
                {
                    if (maxFeePerGas.HasValue || maxPriorityFeePerGas.HasValue)
                        buyTask = accountService.ExactETHForTokens1559Async(SwapFactory, amountIn, amountOutMin, buyPath, gasCount, maxPriorityFeePerGas, maxFeePerGas, repeat, outTime);
                    else
                        buyTask = accountService.ExactETHForTokensAsync(SwapFactory, amountIn, amountOutMin, buyPath, this.gasPrice, gasCount, repeat, outTime);
                }
                else
                {
                    if (maxFeePerGas.HasValue || maxPriorityFeePerGas.HasValue)
                        buyTask = accountService.ExactTokensForTokens1559Async(SwapFactory, amountIn, amountOutMin, buyPath, gasCount, maxPriorityFeePerGas, maxFeePerGas, repeat, outTime);
                    else
                        buyTask = accountService.ExactTokensForTokensAsync(SwapFactory, amountIn, amountOutMin, buyPath, this.gasPrice, gasCount, repeat, outTime);
                }
                buyTasks.Add(buyTask);
            }

            logger.LogInformation("[Buy] sending...");

            foreach (var bucket in TaskHelp.Interleaved(buyTasks))
            {
                var t = await bucket;
                var txs = await t;
                foreach (var tx in TaskHelp.Interleaved(txs))
                {
                    var hashTask = await tx;
                    var hash = await hashTask;
                    logger.LogInformation("  tx={TxHash}", hash);
                }
            }

            // preserve legacy gas for auto-sell unless overridden by config
            sellGas = gasPrice;

            await AfterTokenTransferAsync(buyToken, sellPath);
        }

        private void BeforeTokenTransfer()
        {
            if (IsdelayTime && delayTime > 0)
            {
                Thread.Sleep(delayTime);
            }
            if (accountServices.Count <= 0)
            {
                logger.LogError("[Buy] cannot proceed. account count={Count}", accountServices.Count);
            }
        }

        private async Task AfterTokenTransferAsync(string buyToken, List<string> sellPath)
        {
            if (!(Approve || AutoSell)) return;

            await Task.Run(async () =>
            {
                ulong? sellBlock = null;
                ulong? sellTime = null;

                if (AutoSell)
                {
                    if (!string.IsNullOrEmpty(projectConfig.Sell?.Receive))
                    {
                        sellPath.Add(projectConfig.Sell!.Receive!);
                    }

                    if (IsdelayBlock)
                    {
                        ulong blockNumber = chainService.BlockNumber;
                        var delayBlocks = projectConfig.Sell?.Delay?.BlockDelay ?? 0;
                        if (delayBlocks < 0) delayBlocks = 0;
                        sellBlock = checked(blockNumber + (ulong)delayBlocks);
                    }

                    if (IsdelayTime)
                    {
                        // keep milliseconds for SellTokensForTokensAsync
                        sellTime = (ulong?)(projectConfig.Sell!.Delay!.TimeDelay!.Value * 1000);
                    }

                    if (projectConfig.Sell!.GasPrice.HasValue)
                    {
                        sellGas = new BigInteger(projectConfig.Sell.GasPrice.Value * 1_000_000_000m);
                    }
                }

                if (Approve)
                {
                    foreach (var accountService in accountServices)
                    {
                        if (!accountService.Approves.TryGetValue(buyToken, out _))
                        {
                            var hex = projectConfig.Approve?.Value ?? "0ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
                            var approveValue = BigInteger.Parse($"0{hex}", NumberStyles.HexNumber);

                            await accountService.ApproveAsync(
                                buyToken,
                                projectConfig.Approve?.Spender ?? swapRoute,
                                approveValue);

                            logger.LogInformation("[Approve] sent for token={Token}", buyToken);
                        }
                    }
                }

                if (AutoSell)
                {
                    AutoSell = false; // one-shot autosell

                    foreach (var accountService in accountServices)
                    {
                        _ = Task.Run(async () =>
                        {
                            bool first = true;
                            while (true)
                            {
                                var balance = await accountService.BalanceOfAsync(buyToken);

                                if (balance > 0 && first)
                                {
                                    first = false;

                                    var pct = projectConfig.Sell?.Percentage ?? 100;
                                    if (pct <= 0) pct = 100;

                                    // careful: multiply first to avoid truncation
                                    balance = (balance * pct) / 100;

                                    await accountService.SellTokensForTokensAsync(
                                        SwapFactory,
                                        balance,
                                        new BigInteger(projectConfig.Sell?.AmountOutMin ?? 0),
                                        [.. sellPath],
                                        sellGas,
                                        gasCount,
                                        sellBlock,
                                        sellTime);
                                }

                                if (balance <= 0 && !first)
                                {
                                    return;
                                }

                                Thread.Sleep(50);
                            }
                        });
                    }
                }
            });
        }

        public Task UpdateAsync() => Task.CompletedTask;

        // ---------- helpers ----------
        private static decimal WeiToGwei(BigInteger wei) => (decimal)wei / 1_000_000_000m;
    }
}
