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
        private readonly List<AccountService> accountServices = new List<AccountService>();
        public BurningService(ILogger<InvokeService> logger, AccountManager accountManager, TokenService tokenService, ChainService chainService)
        {
            this.logger = logger;
            this.accountManager = accountManager;
            this.tokenService = tokenService;
            this.chainService = chainService;
        }

        #region Exact
        private BigInteger InvokegasPrice;
        private BigInteger[] SwapgasPrice;
        private BigInteger gasCount;
        private BigInteger amountIn, amountOutMin;
        #endregion

        private string swapRoute, contract;
        private string[] BuyPath, SellPath;
        private string data;

        private uint? StartTime, StartBlock;
        private ulong Duration;

        private BurningConfigModel burningConfigModel;

        private ISwapFactory SwapFactory;
        private ProjectConfig projectConfig;

        public bool EthPair;
        private string BuyContract;

        public Func<Task> BurningMothed;    

        #region 延迟
        private int delayTime, delayBlock;
        #endregion

        private bool Approve;
        private bool AutoSell;
        private BigInteger sellGas;
        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled)
            {
                return;
            }

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
            logger.LogInformation($"[项目名 : {projectConfig.Name}]");
            logger.LogInformation($"    账号组 : {groupList} 账号数量 : {accountServices.Count}");
            logger.LogInformation($"    模式   : {projectConfig.Burning.Action}");
            burningConfigModel = projectConfig.Burning;

            if (projectConfig.Burning.Action.Equals("swap", StringComparison.OrdinalIgnoreCase))
            {
                BurningMothed = SwapTaskAsync;
                SwapConfigModel swapConfig = projectConfig.Burning.Swap;

                swapRoute = projectConfig.SwapRouter.ToLower();
                SwapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

                SwapgasPrice = new BigInteger[burningConfigModel.Swap.GasPrice.Count];
                for (int i = 0; i < SwapgasPrice.Length; i++)
                {
                    ulong gas = burningConfigModel.Swap.GasPrice[i];
                    SwapgasPrice[i] = new BigInteger(gas * 1000000000);
                }
                gasCount = new BigInteger(burningConfigModel.Swap.GasCount.Value);
                amountIn = new BigInteger(swapConfig.AmountIn * await tokenService.TokenDecimalsAsync(swapConfig.Path[0]));
                amountOutMin = new BigInteger(swapConfig.AmountOutMin * await tokenService.TokenDecimalsAsync(swapConfig.Path[1]));

                BuyPath = swapConfig.Path;
                BuyContract = BuyPath[^1];
                EthPair = BuyPath[0].Equals(SwapFactory.ETH, StringComparison.OrdinalIgnoreCase);

                logger.LogInformation($"    路由器  : {swapRoute}");
                logger.LogInformation($"    出价    : {swapConfig.AmountIn}  滑点 : {swapConfig.AmountOutMin}  购买 : {string.Join(" -> ", BuyPath)} ");
            }
            else if (projectConfig.Burning.Action.Equals("invoke", StringComparison.OrdinalIgnoreCase))
            {
                contract = projectConfig.Contract?.ToLower();

                InvokegasPrice = new BigInteger(burningConfigModel.Invoke.GasPrice.Value * 1000000000);
                gasCount = new BigInteger(burningConfigModel.Invoke.GasCount.Value);

                BurningMothed = InvokeTaskAsync;
                data = projectConfig.Burning.Invoke.Data;
                logger.LogInformation($"      合约地址: { contract}");
                logger.LogInformation($"      数据 : {data} ");
            }

            StartTime = burningConfigModel.StartTime;
            StartBlock = burningConfigModel.StartBlock;


            if (burningConfigModel.Delay.TimeDelay.HasValue)
            {
                delayTime = burningConfigModel.Delay.TimeDelay.Value;
            }
            if (burningConfigModel.Delay.BlockDelay.HasValue)
            {
                delayBlock = burningConfigModel.Delay.BlockDelay.Value;
            }

            Duration = burningConfigModel.Duration;

            if (projectConfig.Approve != null)
            {
                Approve = projectConfig.Approve.Enabled;
                logger.LogInformation($"  [自动授权]");
                logger.LogInformation($"      合约      :  {projectConfig.Approve?.Spender ?? swapRoute}  数量 : {projectConfig.Approve?.Value ?? "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}");
            }
            if (projectConfig.Sell != null)
            {
                AutoSell = projectConfig.Sell.Enabled;
                if (AutoSell)
                {
                    var sellList = new List<string>();
                    for (int i = BuyPath.Length - 1; i >= 0; i--)
                    {
                        sellList.Add(BuyPath[i]);
                    }
                    if (projectConfig.Sell.GasPrice.HasValue)
                        sellGas = projectConfig.Sell.GasPrice.Value;
                    else
                        sellGas = SwapgasPrice.First();

                    if (!string.IsNullOrEmpty(projectConfig.Sell.Receive))
                        sellList.Add(projectConfig.Sell.Receive);
                    SellPath = sellList.ToArray();

                    logger.LogInformation($"  [自动出售]");
                    logger.LogInformation($"      出售      :  {string.Join(" -> ", SellPath)}");
                    logger.LogInformation($"      时间延迟  :  {(projectConfig.Sell.Delay.TimeDelay == 0 ? "无" : projectConfig.Sell.Delay.TimeDelay)}           区块延迟 : {(projectConfig.Sell.Delay.BlockDelay == 0 ? "无" : projectConfig.Sell.Delay.BlockDelay)}");
                    logger.LogInformation($"      出售数量  :  {(projectConfig.Sell.Percentage == 0 ? "全部" : projectConfig.Sell.Percentage.Value)}                 滑点数量 : {projectConfig.Sell?.AmountOutMin}");
                    logger.LogInformation($"      手续费    :  {(projectConfig.Sell.GasPrice.HasValue ? projectConfig.Sell.GasPrice : "跟踪")}");
                }
            }
            logger.LogWarning("!!!请仔细核对上方数据!!!");
        }
        public async Task UpdateAsync()
        {
            double start_at = 0, end_at = 0;
            await Task.Run(async () =>
           {
               while (projectConfig.Enabled)
               {
                   ulong blockNumber = chainService.BlockNumber;
                   double unixTimestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                   if (start_at > 0)
                   {
                       await BurningMothed.Invoke();
                       if (unixTimestamp > end_at)
                       {
                           start_at = 0;
                           logger.LogInformation($"[{projectConfig.Name}] 燃烧结束 ");
                           return;
                       }
                       if (delayTime > 0)
                           Thread.Sleep(delayTime);
                       continue;
                   }

                   if (StartBlock.HasValue)
                   {
                       if (blockNumber > StartBlock.Value - 1)
                       {
                           start_at = unixTimestamp;
                           end_at = start_at + Duration;
                           continue;
                       }
                       else
                       {
                           ulong block = StartBlock.Value - blockNumber;
                           logger.LogInformation($"[{projectConfig.Name}] 还需等待 : {block} 块");
                           Thread.Sleep((int)(block * 800));
                       }
                   }

                   if (StartTime.HasValue)
                   {
                       if (unixTimestamp >= StartTime)
                       {
                           start_at = unixTimestamp;
                           end_at = start_at + Duration;
                           continue;
                       }
                       else
                       {
                           double time = StartTime.Value - unixTimestamp;
                           logger.LogInformation($"[{projectConfig.Name}] 还需等待 : {time} 秒");
                           Thread.Sleep((int)(time * 800));
                       }
                   }
               }
           });
            await Task.Run(async () =>
            {
                while (AutoSell || Approve)
                {
                    foreach (AccountService accountService in accountServices)
                    {
                        BigInteger balance = await accountService.BalanceOfAsync(BuyContract);
                        if (balance > 0)
                        {
                            Stop();
                            if (Approve && !accountService.Approves.TryGetValue(BuyContract, out BigInteger bigInteger))
                            {
                                Approve = false;
                                bigInteger = BigInteger.Parse($"0{projectConfig.Approve.Value ?? "0ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"}", NumberStyles.HexNumber);
                                await accountService.ApproveAsync(BuyContract, projectConfig.Approve.Spender ?? swapRoute, bigInteger);
                            }
                            if (AutoSell)
                            {
                                balance = balance * (projectConfig.Sell.Percentage.Value / 100);
                                List<Task> sellTasks = new();
                                ulong? sellBlock = null, sellTime = null;
                                if (projectConfig.Sell.Delay.BlockDelay.HasValue)
                                {
                                    ulong blockNumber = chainService.BlockNumber;
                                    sellBlock = blockNumber + projectConfig.Sell.Delay.BlockDelay.Value;
                                }
                                if (projectConfig.Sell.Delay.TimeDelay.HasValue)
                                {
                                    sellTime = (ulong?)(projectConfig.Sell.Delay.TimeDelay.Value * 1000);
                                }
                                await accountService.SellTokensForTokensAsync(SwapFactory, balance, new BigInteger(projectConfig.Sell.AmountOutMin.Value), SellPath, sellGas, gasCount, sellBlock, sellTime);
                            }
                        }
                    }
                }
            });
        }
        public async Task InvokeTaskAsync()
        {
            List<Task<string>> invokeTasks = new();
            foreach (AccountService accountService in accountServices)
            {
                Task<string> invokeTask = accountService.InvokeAsync(contract, InvokegasPrice, gasCount, data);
                invokeTasks.Add(invokeTask);
            }
            foreach (var bucket in TaskHelp.Interleaved(invokeTasks))
            {
                var t = await bucket;
                string result = await t;
                logger.LogInformation($"    [Invoke]  Tx : {result}");
            }
        }
        public async Task SwapTaskAsync()
        {
            List<Task<string>> buyTasks = new();
            foreach (AccountService accountService in accountServices)
            {
                Task<string> buyTask;
                if (EthPair)
                {
                    for (int i = 0; i < SwapgasPrice.Length; i++)
                    {
                        buyTask = accountService.ExactETHForTokensAsync(SwapFactory, amountIn, amountOutMin, BuyPath, SwapgasPrice[i], gasCount);
                        buyTasks.Add(buyTask);
                    }
                }
                else
                {
                    for (int i = 0; i < SwapgasPrice.Length; i++)
                    {
                        buyTask = accountService.ExactTokensForTokensAsync(SwapFactory, amountIn, amountOutMin, BuyPath, SwapgasPrice[i], gasCount);
                        buyTasks.Add(buyTask);
                    }
                }
            }
            foreach (var bucket in TaskHelp.Interleaved(buyTasks))
            {
                var t = await bucket;
                string result = await t;
                logger.LogInformation($"    [Buy]  Tx : {result}");
            }
        }
        private void Stop()
        {
            projectConfig.Enabled = false;
        }
    }
}
