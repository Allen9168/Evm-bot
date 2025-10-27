using Microsoft.Extensions.Logging;
using SoHa_Bot.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    internal class TransactionService : IProject
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;
        private readonly AccountManager accountManager;
        private readonly TokenService tokenService;
        private readonly ChainService chainService;
        private readonly List<AccountService> accountServices = new List<AccountService>();
        public TransactionService(ILogger<TransactionService> logger, AccountManager accountManager, TokenService tokenService, ChainService chainService)
        {
            this.logger = logger;
            this.accountManager = accountManager;
            this.tokenService = tokenService;
            this.chainService = chainService;
        }


        private ISwapFactory SwapFactory;

        private ProjectConfig projectConfig;

        private string send;
        private bool TransferETHPair;
        private string contract, swapRoute;
        private int repeat;
        private int outTime;
        private string sender;



        private bool EthPair;

        #region Exact
        private bool AutoGas = true;
        private BigInteger gasPrice, gasCount;
        private BigInteger amountIn, amountOutMin;
        private bool IsSlippage;
        #endregion


        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled)
            {
                return;
            }
            logger.LogInformation($" 项目     : {projectConfig.Name}");

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
            logger.LogInformation($"  group : {groupList} account : {accountServices.Count}");
            swapRoute = projectConfig.SwapRouter.ToLower();
            contract = projectConfig.Contract.ToLower();
            logger.LogInformation($" swapRoute : {swapRoute} contract ： {contract}");

            SwapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

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

            sender = projectConfig.Sender.ToLower();

            RobotUser.TransactionEvent += RobotUser_TransactionEventAsync;
        }

        private async  Task RobotUser_TransactionEventAsync(string tx, string from, string gasPrice, string maxFeePerGas, string maxPriorityFeePerGas, string value, string input)
        {
            if (from.Equals(sender))
            {
                if (TransferETHPair)
                {
                    await ETHForTokensAsync(contract, gasPrice);
                }
                else {
                    await TokensForTokensAsync(new string[] { send, contract }, gasPrice);
                }
            }
        }

        private async Task ETHForTokensAsync(string BuyToken, string GasPrice)
        {
            List<Task> tasks = new();
            if (accountServices.Count <= 0)
            {
                logger.LogError($"[Buy] 无法完成交易 账户数量 : {accountServices.Count}");
            }
            if (AutoGas)
            {
                gasPrice = BigInteger.Parse($"0{GasPrice.Remove(0, 2)}", NumberStyles.HexNumber);
            }

            foreach (AccountService accountService in accountServices)
            {
                Task buyTask = accountService.ExactETHForTokensAsync(SwapFactory, amountIn, amountOutMin, new string[] { SwapFactory.ETH, BuyToken }, gasPrice, gasCount, repeat, outTime);
                tasks.Add(buyTask);
            }
            await Task.WhenAll(tasks.ToArray());
        }
        private async Task TokensForTokensAsync(string[] path, string GasPrice)
        {
        
            List<Task> tasks = new();
            if (accountServices.Count <= 0)
            {
                logger.LogError($"[Buy] 无法完成交易 账户数量 : {accountServices.Count}");
            }
            if (AutoGas)
            {
                gasPrice = BigInteger.Parse($"0{GasPrice.Remove(0, 2)}", NumberStyles.HexNumber);
            }

            foreach (AccountService accountService in accountServices)
            {
                Task buyTask = accountService.ExactTokensForTokensAsync(SwapFactory, amountIn, amountOutMin, path, gasPrice, gasCount, repeat, outTime);
                tasks.Add(buyTask);
            }
            await Task.WhenAll(tasks.ToArray());
        }


        public async  Task UpdateAsync()
        {

        }
    }
}
