using Microsoft.Extensions.Logging;
using Soha.Core;
using Soha.Core.Response.Eth;
using Soha.SwapFactory;
using SoHa_Bot.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service.Project
{
    public sealed class InvokeService : IProject
    {
        private readonly ILogger logger;
        private readonly RpcClient rpcClient;
        private readonly TokenService tokenService;
        private readonly AccountManager accountManager;
        private readonly List<AccountService> accountServices = new List<AccountService>();
        private ISwapFactory SwapFactory;

        private ProjectConfig projectConfig;

        private string send;
        private string contract, swapRoute;
        private int repeat;
        private int outTime;


        private bool EthPair;

        #region Exact
        private bool AutoGas = true;
        private BigInteger gasPrice, gasCount;
        private BigInteger amountIn, amountOutMin;
        private bool IsSlippage;
        #endregion

        #region 延迟
        private bool IsdelayTime, IsdelayBlock;
        private int delayTime, delayBlock;
        #endregion

        private string MethodID;
        public InvokeService(ILogger<InvokeService> logger, AccountManager accountManager, TokenService tokenService, RpcClient rpcClient)
        {
            this.logger = logger;
            this.rpcClient = rpcClient;
            this.tokenService = tokenService;
            this.accountManager = accountManager;
        }
        public async Task StartAsync(ProjectConfig projectConfig)
        {
            if (!projectConfig.Enabled)
            {
                return;
            }
            SwapFactory = Soha.SwapFactory.SwapFactory.SwapRouteFactory(swapRoute);

            this.projectConfig = projectConfig;
            logger.LogInformation($" 项目     : {projectConfig.Name}");

            MethodID = projectConfig.Invoke.MethodID.ToLower();
            logger.LogInformation($" MethodID : {MethodID}");

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
            logger.LogInformation($" 交换路由器 : {swapRoute} contract ： {contract}");

            amountIn = new BigInteger(projectConfig.Transaction.Cost * 1000000000000000000L);
            repeat = projectConfig.Transaction.Count;
            outTime = projectConfig.Transaction.OutTime;
            logger.LogInformation($" amountIn : {projectConfig.Transaction.Cost} repeat : {repeat} outTime : {outTime}");

            if (projectConfig.Exact != null)
            {
                if (string.IsNullOrEmpty(projectConfig.Exact.Send))
                {
                    send = SwapFactory.ETH;
                }
                else
                {
                    send = projectConfig.Exact.Send;
                }
                decimal unit = await tokenService.TokenDecimalsAsync(send);

                EthPair = send.Equals(SwapFactory.ETH);
                logger.LogInformation($"  path : {send} => {contract}");
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
                logger.LogInformation($"  amountOutMin {(projectConfig.Exact.AmountOutMin.HasValue ? projectConfig.Exact.AmountOutMin.Value : "无视流动性")} gasPrice : {(projectConfig.Exact.GasPrice.HasValue ? projectConfig.Exact.GasPrice.Value.ToString() : "跟踪模式")}  gasCount : {gasCount}");
            }
            else
            {
                logger.LogError($" 项目 : {projectConfig.Name} 流动性配置不可为空");
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
                logger.LogInformation($" delayTime : {delayTime} delayBlock ： {delayBlock}");
            }
        }
        private async Task ETHForTokensAsync(string BuyToken, string GasPrice)
        {
            if (IsdelayTime)
            {
                Thread.Sleep(delayTime);
            }
            if (IsdelayBlock)
            {
                // Do Some things
            }
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
            if (IsdelayTime)
            {
                Thread.Sleep(delayTime);
            }
            if (IsdelayBlock)
            {
                // Do Some things
            }
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
        public async Task InvokeEventAsync(string Tx, TransactionResponse response, BigInteger Value)
        {
            if (!string.IsNullOrEmpty(send))
            {
                if (!response.from.Equals(send))
                {
                    return;
                }
            }
            if (!string.IsNullOrEmpty(response.to) && response.to.Equals(contract) && !string.IsNullOrEmpty(response.input))
            {
                if (response.input.StartsWith(MethodID))
                {
                    logger.LogInformation($" Invoke : {Tx}");
                    if (EthPair)
                    {
                        await ETHForTokensAsync(contract, response.gasPrice);
                    }
                    else
                    {
                        await TokensForTokensAsync(new string[] { send, contract }, response.gasPrice);
                    }
                }
            }
        }
        public Task UpdateAsync() { return Task.CompletedTask; }
    }
}
