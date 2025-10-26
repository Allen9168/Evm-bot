using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soha.Core;
using Soha.Core.Response.Eth;
using Soha.Model;
using Soha.Model.Event;
using Soha.Service;
using Soha.SwapFactory;
using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;

namespace Soha
{
    public sealed class RobotUser
    {
        private readonly ILogger logger;
        private readonly RpcClient rpcClient;
        private readonly TransactionsService transactionsService;
        private readonly ChainService chainService;

        public RobotUser(IConfiguration configuration, RpcClient rpcClient, ILogger<RobotUser> logger, TransactionsService transactionsService, ChainService chainService)
        {
            this.rpcClient = rpcClient;
            this.logger = logger;
            this.transactionsService = transactionsService;
            this.chainService = chainService;

            monitorConfig = configuration.GetSection("Monitor").Get<MonitorConfig>();
            LiquidityEnabled = configuration.GetValue<bool>("Liquidity:Enabled");
            TranscationEnabled = monitorConfig.Enabled;
        }

        public delegate Task Transfer(string TransactionHash, string From, string To, string Value, string InputData);
        public event Transfer TransferEvent;
#nullable enable
        public  delegate Task Transaction(string tx, string from, string gasPrice, string? maxFeePerGas, string? maxPriorityFeePerGas, string value, string input);
        public static event Transaction? TransactionEvent;
#nullable disable 

        private readonly MonitorConfig monitorConfig;

        private readonly bool TranscationEnabled;
        private readonly bool LiquidityEnabled;

        public async Task StartAsync()
        {
            rpcClient.AddLocalRpcMethod("eth_subscription", new Func<string, NewHeads, Task>(NewHeadsAsync));
            rpcClient.AddLocalRpcMethod("eth_subscription", new Func<string, string, Task>(NewPendingTransactionsAsync));
            rpcClient.StartListening();
            await rpcClient.SubscribeAsync("newHeads");                // 订阅新的区块
            await rpcClient.SubscribeAsync("newPendingTransactions");  // 订阅新的交易
            chainService.BlockNumber = await rpcClient.BlockNumberAsync();
        }
        private async Task NewHeadsAsync(string subscription, NewHeads result)
        {
            await Task.Run(() =>
            {
                BigInteger number = BigInteger.Parse($"0{result.number.Remove(0, 2)}", NumberStyles.HexNumber);
                chainService.BlockNumber = (ulong)number;
            });
        }
        private async Task NewPendingTransactionsAsync(string subscription, string result)
        {
            TransactionResponse Response = await rpcClient.InvokeAsync<TransactionResponse>("eth_getTransactionByHash", new string[] { result });
            if (Response == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(Response.input))
            {
                if (LiquidityEnabled && !string.IsNullOrEmpty(Response.to))
                {
                    if (SwapFactory.SwapFactory.SwapFactoryDictionary.TryGetValue(Response.to, out ISwapFactory swapFactory) && Response.input.Length >= 10)
                    {
                        await swapFactory.SwapInvokeAsync(result, Response.from, Response.gasPrice, Response.maxFeePerGas, Response.maxPriorityFeePerGas, Response.value, Response.input);
                    }
                }
                if (TranscationEnabled)
                {
                    if (Response.input.Equals("0xa9059cbb") || Response.input.Equals("0x"))
                    {
                        await TransferEvent(result, Response.from, Response.to, Response.value, Response.input);
                    }
                }
                if(TransactionEvent!=null)
                
                await TransactionEvent(result, Response.from, Response.gasPrice, Response.maxFeePerGas, Response.maxPriorityFeePerGas, Response.value, Response.input);            }
        }
    }
}