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
using System.Threading;

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
        private long _pendingSeen;
        private long _nullSeen;
        private PeriodicTimer _statsTimer;
        private CancellationTokenSource _statsCts = new();

        public async Task StartAsync()
        {
            rpcClient.AddLocalRpcMethod("eth_subscription", new Func<string, NewHeads, Task>(SafeNewHeadsAsync));
            rpcClient.AddLocalRpcMethod("eth_subscription", new Func<string, string, Task>(SafeNewPendingTransactionsAsync));
            try
            {
                rpcClient.StartListening();
                await rpcClient.SubscribeAsync("newHeads");                // ****
                await rpcClient.SubscribeAsync("newPendingTransactions");  // ****
                chainService.BlockNumber = await rpcClient.BlockNumberAsync();
                logger.LogInformation("[RobotUser] Subscribed: newHeads + newPendingTransactions");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RobotUser] Failed to start subscriptions");
                throw;
            }

            // === Added: once-per-second aggregated stats to avoid log flood ===
            _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = Task.Run(async () =>
            {
                while (await _statsTimer.WaitForNextTickAsync(_statsCts.Token))
                {
                    var p = Interlocked.Exchange(ref _pendingSeen, 0);
                    var n = Interlocked.Exchange(ref _nullSeen, 0);
                    logger.LogInformation("[RobotUser] Last 1s: pendingTx={Pending} getTx=null={Null}", p, n);
                }
            }, _statsCts.Token);
        }
        private async Task SafeNewHeadsAsync(string subscription, NewHeads result)
        {
            try { await NewHeadsAsync(subscription, result); }
            catch (Exception ex) { logger.LogError(ex, "[RobotUser] NewHeads callback error"); }
        }
        private async Task SafeNewPendingTransactionsAsync(string subscription, string result)
        {
            try { await NewPendingTransactionsAsync(subscription, result); }
            catch (StreamJsonRpc.ConnectionLostException ex) { logger.LogWarning(ex, "[RobotUser] Subscription connection lost (pendingTx)"); }
            catch (AggregateException aex)
            {
                foreach (var ex in aex.Flatten().InnerExceptions)
                    logger.LogError(ex, "[RobotUser] PendingTx callback aggregate inner error");
            }
            catch (Exception ex) { logger.LogError(ex, "[RobotUser] PendingTx callback error"); }
        }
        private async Task NewHeadsAsync(string subscription, NewHeads result)
        {
            try
            {
                await Task.Run(() =>
                {
                    BigInteger number = BigInteger.Parse($"0{result.number.Remove(0, 2)}", NumberStyles.HexNumber);
                    chainService.BlockNumber = (ulong)number;
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RobotUser] NewHeads processing error");
            }
        }
        private async Task NewPendingTransactionsAsync(string subscription, string result)
        {
            try
            {
                logger.LogDebug("[RobotUser] pendingTx: tx={Tx}", result);
                Interlocked.Increment(ref _pendingSeen);

                TransactionResponse Response = await rpcClient.InvokeAsync<TransactionResponse>(
                    "eth_getTransactionByHash", new string[] { result });
                if (Response == null)
                {
                    logger.LogDebug("[RobotUser] getTransactionByHash returned null: tx={Tx}", result);
                    Interlocked.Increment(ref _nullSeen);
                    return;
                }
                if (!string.IsNullOrEmpty(Response.input))
                {
                    if (LiquidityEnabled && !string.IsNullOrEmpty(Response.to))
                    {
                        if (SwapFactory.SwapFactory.SwapFactoryDictionary.TryGetValue(Response.to, out ISwapFactory swapFactory)
                            && Response.input.Length >= 10)
                        {
                            try
                            {
                                await swapFactory.SwapInvokeAsync(result, Response.from, Response.gasPrice, Response.maxFeePerGas, Response.maxPriorityFeePerGas, Response.value, Response.input);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "[RobotUser] SwapInvokeAsync failed tx={Tx}", result);
                            }
                        }
                    }
                    if (TranscationEnabled)
                    {
                        if (Response.input.Equals("0xa9059cbb", StringComparison.OrdinalIgnoreCase)
                            || Response.input.Equals("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (TransferEvent != null)
                            {
                                try
                                {
                                    await TransferEvent(result, Response.from, Response.to, Response.value, Response.input);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "[RobotUser] TransferEvent handler failed tx={Tx}", result);
                                }
                            }
                        }
                    }
                    if (TransactionEvent != null)
                    {
                        try
                        {
                            await TransactionEvent(result, Response.from, Response.gasPrice, Response.maxFeePerGas, Response.maxPriorityFeePerGas, Response.value, Response.input);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "[RobotUser] TransactionEvent handler failed tx={Tx}", result);
                        }
                    }
                }
            }
            catch (StreamJsonRpc.ConnectionLostException ex)
            {
                logger.LogWarning(ex, "[RobotUser] Connection lost during getTransactionByHash tx={Tx}", result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RobotUser] PendingTx processing error tx={Tx}", result);
            }
        }
    }
}