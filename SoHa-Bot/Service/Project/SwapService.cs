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
    public class SwapService : IProject
    {
        private readonly ILogger logger;
        private readonly TokenService tokenService;
        private readonly ChainService chainService;
        private readonly AccountManager accountManager;
        private readonly List<AccountService> accountServices = new List<AccountService>();
        public SwapService(ILogger<SwapService> logger, AccountManager accountManager, TokenService tokenService, ChainService chainService)
        {
            this.logger = logger;
            this.tokenService = tokenService;
            this.chainService = chainService;
            this.accountManager = accountManager;
        }

        private ISwapFactory SwapFactory;
        private ProjectConfig projectConfig;

        private string swapRoute;
        private string contract;
        private string send;

        private int repeat;
        private int outTime;

        private Dictionary<string, decimal> Liquidity;

        private decimal LiquidityETHAmount;
        private bool LiquidityEnabled, LiquidityETHEnabled;

        #region Exact
        private BigInteger gasPrice, gasCount;
        private BigInteger amountIn, amountOutMin;
        private bool IsSlippage;
        #endregion

        #region 延迟
        private bool IsdelayTime, IsdelayBlock;
        private int delayTime, delayBlock;
        #endregion

        private bool AutoGas = true;
        private bool Approve;
        private bool AutoSell;
        private BigInteger sellGas;

        private bool TransferETHPair;

        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled)
            {
                return;
            }

            swapRoute = projectConfig.SwapRouter.ToLower();
            contract = projectConfig.Contract.ToLower();
            logger.LogInformation($"      swapRoute : {swapRoute} contract : {contract}");
            SwapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

            this.projectConfig = projectConfig;
            string groupList = string.Empty;
            foreach (string accountGroup in projectConfig.AccountGroups)
            {
                List<AccountService> accountServices = accountManager.GetAccount(accountGroup);
                this.accountServices.AddRange(accountServices);
                if (string.IsNullOrEmpty(groupList))
                {
                    groupList = string.Concat(groupList, accountGroup);
                }
                else
                {
                    groupList = string.Concat(groupList, ",", accountGroup);
                }
            }


            if (projectConfig.Exact != null)
            {
                if (string.IsNullOrEmpty(projectConfig.Exact.Send))
                {
                    send = SwapFactory.ETH;
                    TransferETHPair = true;
                }
                else
                {
                    send = projectConfig.Exact.Send;
                    if (send.Equals(SwapFactory.ETH, StringComparison.OrdinalIgnoreCase))
                    {
                        TransferETHPair = true;
                    }
                }
                decimal unit = await tokenService.TokenDecimalsAsync(send);

                IsSlippage = projectConfig.Exact.AmountOutMin.HasValue;
                if (IsSlippage)
                {
                    amountOutMin = new BigInteger(projectConfig.Exact.AmountOutMin.Value * unit);
                }
                if (projectConfig.Exact.GasPrice.HasValue)
                {
                    AutoGas = false;
                    gasPrice = new BigInteger(projectConfig.Exact.GasPrice.Value * 1000000000);
                }
                gasCount = new BigInteger(projectConfig.Exact.GasCount.Value);
                string amountOutMinStr = amountOutMin > 0 ? amountOutMin.ToString() : "null";
                logger.LogInformation($"      amountOutMin {amountOutMinStr} gasPrice : {(gasPrice > 0 ? gasPrice / 1000000000 : "跟踪模式")}");

                amountIn = new BigInteger(projectConfig.Transaction.Cost * unit);
                repeat = projectConfig.Transaction.Count;
                outTime = projectConfig.Transaction.OutTime;
                logger.LogInformation($"      amountIn : {projectConfig.Transaction.Cost} repeat : {repeat} outTime : {outTime}");
                logger.LogInformation($"      group : {groupList} account : {accountServices.Count}");
            }
            else
            {
                logger.LogError($" 项目 : {projectConfig.Name} 流动性配置不可为空");
                return;
            }

            if (projectConfig.Delay != null)
            {
                IsdelayTime = projectConfig.Delay.TimeDelay.HasValue;
                if (IsdelayTime)
                {
                    delayTime = projectConfig.Delay.TimeDelay.Value * 1000;
                }
                IsdelayBlock = projectConfig.Delay.BlockDelay.HasValue;
                if (IsdelayBlock)
                {
                    delayBlock = projectConfig.Delay.BlockDelay.Value;
                }
                logger.LogInformation($"      delayTime : {delayTime} delayBlock ： {delayBlock}");
            }

            if (projectConfig.Liquiditys.Liquidity != null)
            {
                LiquidityEnabled = projectConfig.Liquiditys.Liquidity.Enabled;
                if (LiquidityEnabled)
                {
                    Liquidity = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    SwapFactory.AddLiquidityEvent += SwapFactory_AddLiquidityEventAsync;
                    logger.LogInformation($"    [Liquidity]");
                    foreach (string token in projectConfig.Liquiditys.Liquidity.Token)
                    {
                        var Decima = await tokenService.TokenDecimalsAsync(token);
                        Liquidity.Add(token, projectConfig.Liquiditys.Liquidity.Amount * Decima);
                        logger.LogInformation($"        Token : {token}  数量 : {projectConfig.Liquiditys.Liquidity.Amount}");
                    }
                }
            }

            if (projectConfig.Liquiditys.LiquidityETH != null)
            {
                LiquidityETHEnabled = projectConfig.Liquiditys.LiquidityETH.Enabled;
                if (LiquidityETHEnabled)
                {
                    var Decima = await tokenService.TokenDecimalsAsync(SwapFactory.ETH);
                    LiquidityETHAmount = projectConfig.Liquiditys.LiquidityETH.Amount * Decima;
                    SwapFactory.AddLiquidityETHEvent += SwapFactory_AddLiquidityETHEventAsync;
                    logger.LogInformation($"    [LiquidityETH]");
                    logger.LogInformation($"        Token : {SwapFactory.ETH}  数量 : {projectConfig.Liquiditys.LiquidityETH.Amount}");
                }
            }

            if (projectConfig.Approve != null)
            {
                Approve = projectConfig.Approve.Enabled;
                logger.LogInformation($"    [Approve]");
                logger.LogInformation($"        Spender  :  {projectConfig.Approve?.Spender}  Value : {projectConfig.Approve?.Value}");
            }

            if (projectConfig.Sell != null)
            {
                AutoSell = projectConfig.Sell.Enabled;
                logger.LogInformation($"  [自动出售]");
                logger.LogInformation($"      时间延迟  :  {(projectConfig.Sell.Delay.TimeDelay == 0 ? "无" : projectConfig.Sell.Delay.TimeDelay)}           区块延迟 : {(projectConfig.Sell.Delay.BlockDelay == 0 ? "无" : projectConfig.Sell.Delay.BlockDelay)}");
                logger.LogInformation($"      出售数量  :  {(projectConfig.Sell.Percentage == 100 ? "全部" : projectConfig.Sell.Percentage.Value)}           获得数量 : {projectConfig.Sell?.AmountOutMin}");
                logger.LogInformation($"      手续费    :  {(projectConfig.Sell.GasPrice.HasValue ? projectConfig.Sell.GasPrice : "跟踪")}");
            }
        }
        public Task StopAsync()
        {
            if (LiquidityEnabled)
            {
                SwapFactory.AddLiquidityEvent += SwapFactory_AddLiquidityEventAsync;
            }
            if (LiquidityETHEnabled)
            {
                SwapFactory.AddLiquidityETHEvent -= SwapFactory_AddLiquidityETHEventAsync;
            }
            return Task.CompletedTask;
        }
        private async Task SwapFactory_AddLiquidityETHEventAsync(string tx, string from, BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, decimal value, string token, decimal amountTokenDesired)
        {
            if (token.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                if (value >= LiquidityETHAmount)
                {
                    await TokenTransferAsync(gasPrice, maxFeePerGas, maxPriorityFeePerGas, SwapFactory.ETH, token);
                }
                else
                {
                    logger.LogError($"    [LiquidityETH] 底池检测 需要 ：{LiquidityETHAmount}");
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
#pragma warning disable VSTHRD103 // 当在异步方法中时，调用异步方法
                    logger.LogInformation($"[LiquidityETH] tx : {tx} from : {from} gasPrice : {gasPrice / 1000000000} value : {value / ethDecimalTask.Result } token : {tokenNameTask.Result} amountTokenDesired : { amountTokenDesired / tokenDecimalTask.Result}");
#pragma warning restore VSTHRD103 // 当在异步方法中时，调用异步方法
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            });
        }
        private async Task SwapFactory_AddLiquidityEventAsync(string tx, string from, BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, string tokenA, string tokenB, decimal amountADesired, decimal amountBDesired)
        {
            string buyToken = string.Empty, routeToken = string.Empty;
            decimal amount = 0, balance = 0;
            bool DoBuy = false;
            if (tokenA.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                amount = amountADesired;
                balance = amountBDesired;
                routeToken = tokenB;
                buyToken = tokenA;
                DoBuy = true;
            }
            else if (tokenB.Equals(contract, StringComparison.OrdinalIgnoreCase))
            {
                balance = amountADesired;
                amount = amountBDesired;
                routeToken = tokenA;
                buyToken = tokenB;
                DoBuy = true;
            }
            if (DoBuy)
            {
                if (Liquidity.TryGetValue(routeToken, out decimal value))
                {
                    if (balance >= value)
                    {
                        await TokenTransferAsync(gasPrice, maxFeePerGas, maxPriorityFeePerGas, routeToken, buyToken);
                    }
                    else
                    {
                        logger.LogError($"      数量 : {balance} 检测需要 {value}");
                    }
                }
                else
                {
                    logger.LogError($"      使用了 : {routeToken} 未知的货币添加流动性");
                }
            }
            await Task.Run(async () =>
            {
                try
                {
                    var NameATask = tokenService.TokenNameAsync(tokenA);
                    var NameBTask = tokenService.TokenNameAsync(tokenB);
                    var DecimaATask = tokenService.TokenDecimalsAsync(tokenA);
                    var DecimaBTask = tokenService.TokenDecimalsAsync(tokenB);
                    await Task.WhenAll(NameATask, NameBTask, DecimaATask, DecimaBTask);
#pragma warning disable VSTHRD103 // 当在异步方法中时，调用异步方法
                    logger.LogInformation($"[Liquidity] tx : {tx} from : {from} gasPrice : {gasPrice / 1000000000} tokenA : {NameATask.Result} tokenB : {NameBTask.Result} amountADesired : {amountADesired / DecimaATask.Result} amountBDesired : {amountBDesired / DecimaBTask.Result}");
#pragma warning restore VSTHRD103 // 当在异步方法中时，调用异步方法
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            });
        }
        private async Task TokenTransferAsync(BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, string routeToken, string buyToken)
        {
            beforeTokenTransfe();
            if (AutoGas)
            {
                this.gasPrice = gasPrice;
            }
            List<Task<List<Task<string>>>> buyTasks = new();
            string[] buyPath;
            List<string> sellPath;
            if (send.Equals(routeToken, StringComparison.OrdinalIgnoreCase))
            {
                buyPath = new string[] { routeToken, buyToken };
                sellPath = new List<string> { buyToken, routeToken };
            }
            else
            {
                buyPath = new string[] { send, routeToken, buyToken };
                sellPath = new List<string> { buyToken, routeToken, send };
            }
            foreach (AccountService accountService in accountServices)
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
            logger.LogInformation($"[Buy]");
            foreach (var bucket in TaskHelp.Interleaved(buyTasks))
            {
                var t = await bucket;
                List<Task<string>> txs = await t;  // 最先完成的账号
                foreach (var tx in TaskHelp.Interleaved(txs))
                {
                    Task<string> hash = await tx;
                    logger.LogInformation($"    Tx   : {await hash}");
                }
            }
            sellGas = gasPrice;
            await afterTokenTransferAsync(buyToken, gasPrice, maxFeePerGas, maxPriorityFeePerGas, sellPath);
        }
        private void beforeTokenTransfe()
        {
            if (IsdelayTime && delayTime > 0)
            {
                Thread.Sleep(delayTime);
            }
            if (accountServices.Count <= 0)
            {
                logger.LogError($"[Buy] 无法完成交易 账户数量 : {accountServices.Count}");
            }
        }
        private async Task afterTokenTransferAsync(string buyToken, BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, List<string> sellPath)
        {
            if (Approve || AutoSell)
            {
                await Task.Run(async () =>
                {
                    ulong? sellBlock = null, sellTime = null;
                    if (AutoSell)
                    {
                        if (!string.IsNullOrEmpty(projectConfig.Sell.Receive))
                        {
                            sellPath.Add(projectConfig.Sell.Receive);
                        }
                        if (IsdelayBlock)
                        {
                            ulong blockNumber = chainService.BlockNumber;
                            sellBlock = blockNumber + projectConfig.Sell.Delay.BlockDelay.Value;
                        }
                        if (IsdelayTime)
                        {
                            sellTime = (ulong?)(projectConfig.Sell.Delay.TimeDelay.Value * 1000);
                        }
                        if (projectConfig.Sell.GasPrice.HasValue)
                        {
                            sellGas = projectConfig.Sell.GasPrice.Value;
                        }
                    }
                    if (Approve)
                    {
                        foreach (AccountService accountService in accountServices)
                        {
                            if (!accountService.Approves.TryGetValue(buyToken, out BigInteger bigInteger))
                            {
                                bigInteger = BigInteger.Parse($"0{projectConfig.Approve.Value ?? "0ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}", NumberStyles.HexNumber);
                                await accountService.ApproveAsync(buyToken, projectConfig.Approve.Spender ?? swapRoute, bigInteger);
                            }
                        }
                    }
                    if (AutoSell)
                    {
                        AutoSell = false;
                        foreach (AccountService accountService in accountServices)
                        {
                            _ = Task.Run(async () =>
                              {
                                  bool flag = true;
                                  while (true)
                                  {
                                      BigInteger balance = await accountService.BalanceOfAsync(buyToken);
                                      if (balance > 0 && flag)
                                      {
                                          flag = false;
                                          balance = (projectConfig.Sell.Percentage.Value / 100) * balance;
                                          await accountService.SellTokensForTokensAsync(SwapFactory, balance, new BigInteger(projectConfig.Sell.AmountOutMin.Value), sellPath.ToArray(), sellGas, gasCount, sellBlock, sellTime);
                                      }
                                      if (balance <= 0 && flag == false)
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
        }
        public Task UpdateAsync() { return Task.CompletedTask; }
    }
}
