using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Soha.Core;
using Soha.Model;
using Soha.Model.Event;
using Soha.Service;
using Soha.SwapFactory;
using StreamJsonRpc;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Soha
{
    public sealed class RobotUser : IDisposable
    {
        private readonly ILogger logger;
        private readonly RpcClient rpcClient;
        private readonly TransactionsService transactionsService;
        private readonly ChainService chainService;

        public RobotUser(
            IConfiguration configuration,
            RpcClient rpcClient,
            ILogger<RobotUser> logger,
            TransactionsService transactionsService,
            ChainService chainService)
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
        public delegate Task Transaction(string tx, string from, string gasPrice, string? maxFeePerGas, string? maxPriorityFeePerGas, string value, string input);
        public static event Transaction? TransactionEvent;
#nullable disable

        private readonly MonitorConfig monitorConfig;
        private readonly bool TranscationEnabled;
        private readonly bool LiquidityEnabled;

        // lifecycle / cancellation
        private readonly CancellationTokenSource _cts = new();
        private readonly CancellationTokenSource _statsCts = new();
        private volatile bool _disposed;

        // stats
        private long _pendingSeen;
        private long _txOk;
        private long _nullSeen;
        private long _connLost;
        private string _connLostLastTx = "";
        private long _rateLimited;

        // NEW: count how many TransactionEvent we actually fired (VIP + queue)
        private long _txEventFired;

        private PeriodicTimer _statsTimer;

        // pending queue + worker
        private readonly ConcurrentQueue<string> _pendingQ = new();
        private readonly SemaphoreSlim _pendingWorkerGate = new(1, 1);

        // Tuning knobs (adjust for your RPC limits and machine)
        private const int _lookupParallelism = 300; // number of concurrent detail fetchers
        private int _delayMs = 0;                 // initial per-item delay before fetch
        private const int _delayMinMs = 0;
        private const int _delayMaxMs = 600;

        // VIP fast-lane and de-dup
        private static readonly ConcurrentDictionary<string, byte> _vipSenders =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _vipProbeSlots = new(2, 2); // tiny concurrency for VIP probe
        private readonly ConcurrentDictionary<string, byte> _eventFired =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task StartAsync()
        {
            // Single router to avoid signature ambiguity (result may be string or object)
            rpcClient.AddLocalRpcMethod("eth_subscription", new Func<string, JToken, Task>(EthSubscriptionRouterAsync));

            try
            {
                rpcClient.StartListening();
                await rpcClient.SubscribeAsync("newHeads");
                await rpcClient.SubscribeAsync("newPendingTransactions");
                chainService.BlockNumber = await rpcClient.BlockNumberAsync();
                logger.LogInformation("[RobotUser] Subscribed: newHeads + newPendingTransactions");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[RobotUser] Failed to start subscriptions");
                throw;
            }

            // Aggregated stats (every 3s)
            _statsTimer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _statsTimer.WaitForNextTickAsync(_statsCts.Token))
                    {
                        var p = Interlocked.Exchange(ref _pendingSeen, 0);
                        var ok = Interlocked.Exchange(ref _txOk, 0);
                        var n = Interlocked.Exchange(ref _nullSeen, 0);
                        var rl = Interlocked.Exchange(ref _rateLimited, 0);
                        var fired = Interlocked.Exchange(ref _txEventFired, 0);
                        var hit = p > 0 ? ok * 100.0 / p : 0.0;
                        var d = Volatile.Read(ref _delayMs);
                        var q = _pendingQ.Count;

                        logger.LogInformation(
                            "[RobotUser] Last window: 接收到的tx数量={Pending} 成功分析={Ok} 空tx={Null} 完成率={Hit:F1}% delay={Delay}ms rateLimited={RL} 待分析数量={Queue} fired={Fired}",
                            p, ok, n, hit, d, rl, q, fired
                        );

                        var lost = Interlocked.Exchange(ref _connLost, 0);
                        if (lost > 0)
                        {
                            var lastTx = Volatile.Read(ref _connLostLastTx);
                            logger.LogWarning("[RobotUser] Last window: RPC 链接丢失 lost x{Count}; lastTx={Tx}", lost, lastTx);
                        }

                        // Simple auto-tune on delay, balancing null ratio and rate limits
                        var curDelay = Volatile.Read(ref _delayMs);
                        var nullRatio = p > 0 ? (double)n / p : 0.0;
                        if (rl > 0 || nullRatio > 0.15)
                        {
                            var next = Math.Min(curDelay + 40, _delayMaxMs);
                            Volatile.Write(ref _delayMs, next);
                        }
                        else
                        {
                            var next = Math.Max(curDelay - 30, _delayMinMs);
                            Volatile.Write(ref _delayMs, next);
                        }
                    }
                }
                catch (OperationCanceledException) { /* normal on shutdown */ }
                catch (ObjectDisposedException) { /* timer disposed */ }
                catch (Exception ex)
                {
                    if (!_cts.IsCancellationRequested && !_disposed)
                        logger.LogError(ex, "[RobotUser] Stats loop error");
                }
            }, _statsCts.Token);
        }

        /// <summary>
        /// Allow services to register VIP senders that should bypass the queue.
        /// </summary>
        public static void AddVipSender(string senderAddress)
        {
            if (!string.IsNullOrWhiteSpace(senderAddress))
                _vipSenders.TryAdd(senderAddress, 0);
        }

        /// <summary>
        /// Router for eth_subscription payloads. Handles both pending tx (string/object) and newHeads.
        /// </summary>
        private async Task EthSubscriptionRouterAsync(string subscription, JToken result)
        {
            if (_cts.IsCancellationRequested || _disposed) return;

            try
            {
                // pending tx case (string txhash or object with "hash")
                if (result.Type == JTokenType.String || result["hash"] != null)
                {
                    var tx = result.Type == JTokenType.String ? result.Value<string>() : result["hash"]?.Value<string>();
                    if (!string.IsNullOrEmpty(tx))
                    {
                        // VIP fast probe (immediate getTransactionByHash): if from is VIP, fire now
                        _ = TryVipProbeAsync(tx);
                        await SafeNewPendingTransactionsAsync(subscription, tx!);
                    }
                    return;
                }

                // newHeads case
                var head = result.ToObject<NewHeads>();
                if (head != null)
                {
                    await SafeNewHeadsAsync(subscription, head);
                    return;
                }

                logger.LogTrace("[RobotUser] eth_subscription: unknown payload shape");
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] EthSubscriptionRouterAsync error");
            }
        }

        private async Task SafeNewHeadsAsync(string subscription, NewHeads result)
        {
            try { await NewHeadsAsync(subscription, result); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] NewHeads callback error");
            }
        }

        private async Task SafeNewPendingTransactionsAsync(string subscription, string result)
        {
            try { await NewPendingTransactionsAsync(subscription, result); }
            catch (ConnectionLostException)
            {
                Interlocked.Increment(ref _connLost);
                Volatile.Write(ref _connLostLastTx, result);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (AggregateException aex)
            {
                foreach (var ex in aex.Flatten().InnerExceptions)
                {
                    if (!_cts.IsCancellationRequested && !_disposed)
                        logger.LogError(ex, "[RobotUser] PendingTx callback aggregate inner error");
                }
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] PendingTx callback error");
            }
        }

        private async Task NewHeadsAsync(string subscription, NewHeads result)
        {
            if (_cts.IsCancellationRequested || _disposed) return;

            try
            {
                await Task.Run(() =>
                {
                    BigInteger number = BigInteger.Parse($"0{result.number.Remove(0, 2)}", NumberStyles.HexNumber);
                    chainService.BlockNumber = (ulong)number;
                }, _cts.Token);

                // Optional: block-level processing can be added here if you want a block fallback.
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] NewHeads processing error");
            }
        }

        private Task NewPendingTransactionsAsync(string subscription, string txHash)
        {
            if (_cts.IsCancellationRequested || _disposed) return Task.CompletedTask;

            try
            {
                Interlocked.Increment(ref _pendingSeen);
                _pendingQ.Enqueue(txHash);
                _ = EnsurePendingWorkerAsync(); // fire-and-forget
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] PendingTx enqueue error tx={Tx}", txHash);
            }
            return Task.CompletedTask;
        }

        // Immediate VIP probe with retries: if tx.from is VIP, raise event right away (no queue).
        private async Task TryVipProbeAsync(string txHash)
        {
            if (_cts.IsCancellationRequested || _disposed) return;
            if (!await _vipProbeSlots.WaitAsync(0)) return; // limit VIP concurrency

            try
            {
                // up to 6 quick attempts within ~600ms with small backoff
                const int maxAttempts = 6;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var jt = await rpcClient.InvokeAsync<JToken>("eth_getTransactionByHash", new object[] { txHash });
                        if (jt == null || jt.Type == JTokenType.Null)
                        {
                            await Task.Delay(50 * attempt, _cts.Token);
                            continue;
                        }

                        var from = jt["from"]?.Value<string>() ?? string.Empty;
                        if (string.IsNullOrEmpty(from)) return;
                        if (!_vipSenders.ContainsKey(from)) return;

                        // de-dup: fire only once (VIP or queue)
                        if (!_eventFired.TryAdd(txHash, 0)) return;

                        var gasPrice = jt["gasPrice"]?.Value<string>() ?? string.Empty;
                        var maxFeePerGas = jt["maxFeePerGas"]?.Value<string>() ?? string.Empty;
                        var maxPriorityFeePerGas = jt["maxPriorityFeePerGas"]?.Value<string>() ?? string.Empty;
                        var value = jt["value"]?.Value<string>() ?? string.Empty;
                        var input = jt["input"]?.Value<string>() ?? "0x";

                        logger.LogInformation("[RobotUser] VIP hit: from={From} tx={Tx}", from, txHash);

                        if (TransactionEvent != null)
                        {
                            await TransactionEvent(txHash, from, gasPrice, maxFeePerGas, maxPriorityFeePerGas, value, input);
                            Interlocked.Increment(ref _txEventFired);
                        }
                        return; // done
                    }
                    catch (RemoteInvocationException)
                    {
                        Interlocked.Increment(ref _rateLimited);
                        await Task.Delay(80 * attempt, _cts.Token);
                    }
                    catch (ConnectionLostException)
                    {
                        Interlocked.Increment(ref _connLost);
                        Volatile.Write(ref _connLostLastTx, txHash);
                        await Task.Delay(80 * attempt, _cts.Token);
                    }
                }
                // after attempts, give up (queue worker will still try later)
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] VIP probe error tx={Tx}", txHash);
            }
            finally
            {
                _vipProbeSlots.Release();
            }
        }

        // Lightweight projection (we read only needed fields from the JToken)
        private sealed class TxLite
        {
            public string hash;
            public string from;
            public string to;
            public string input;
            public string gasPrice;
            public string maxFeePerGas;
            public string maxPriorityFeePerGas;
            public string value;
        }

        // Background worker: fetch details with small delay & parallelism, then dispatch
        private async Task EnsurePendingWorkerAsync()
        {
            if (!await _pendingWorkerGate.WaitAsync(0)) return; // already running
            try
            {
                var workers = new Task[_lookupParallelism];
                for (int i = 0; i < _lookupParallelism; i++)
                {
                    workers[i] = Task.Run(async () =>
                    {
                        int idleTicks = 0;
                        while (!_cts.IsCancellationRequested && !_disposed)
                        {
                            if (!_pendingQ.TryDequeue(out var txHash))
                            {
                                try { await Task.Delay(40, _cts.Token); }
                                catch { break; }

                                if (_pendingQ.IsEmpty && ++idleTicks >= 50) break; // ~2s idle
                                continue;
                            }

                            idleTicks = 0; // got work

                            try
                            {
                                int wait = Volatile.Read(ref _delayMs);
                                if (wait > 0)
                                    await Task.Delay(wait, _cts.Token);

                                var jt = await rpcClient.InvokeAsync<JToken>("eth_getTransactionByHash", new object[] { txHash });
                                if (jt == null || jt.Type == JTokenType.Null)
                                {
                                    Interlocked.Increment(ref _nullSeen);
                                    continue;
                                }

                                var resp = new TxLite
                                {
                                    hash = jt["hash"]?.Value<string>() ?? string.Empty,
                                    from = jt["from"]?.Value<string>() ?? string.Empty,
                                    to = jt["to"]?.Value<string>() ?? string.Empty,
                                    input = jt["input"]?.Value<string>() ?? string.Empty,
                                    gasPrice = jt["gasPrice"]?.Value<string>() ?? string.Empty,
                                    maxFeePerGas = jt["maxFeePerGas"]?.Value<string>() ?? string.Empty,
                                    maxPriorityFeePerGas = jt["maxPriorityFeePerGas"]?.Value<string>() ?? string.Empty,
                                    value = jt["value"]?.Value<string>() ?? string.Empty
                                };

                                Interlocked.Increment(ref _txOk);

                                if (string.IsNullOrEmpty(resp.input))
                                    resp.input = "0x";

                                // Liquidity path
                                if (LiquidityEnabled && !string.IsNullOrEmpty(resp.to) && resp.input.Length >= 10)
                                {
                                    if (SwapFactory.SwapFactory.SwapFactoryDictionary.TryGetValue(resp.to, out ISwapFactory swapFactory))
                                    {
                                        try
                                        {
                                            await swapFactory.SwapInvokeAsync(
                                                txHash, resp.from, resp.gasPrice, resp.maxFeePerGas, resp.maxPriorityFeePerGas, resp.value, resp.input);
                                        }
                                        catch (Exception ex)
                                        {
                                            if (!_cts.IsCancellationRequested && !_disposed)
                                                logger.LogError(ex, "[RobotUser] SwapInvokeAsync failed tx={Tx}", txHash);
                                        }
                                    }
                                }

                                // Transfer path (optional semantics)
                                if (TransferEvent != null)
                                {
                                    try
                                    {
                                        // pass Value (resp.value) as the 4th arg, then InputData (resp.input)
                                        await TransferEvent(txHash, resp.from, resp.to, resp.value, resp.input);
                                    }
                                    catch (Exception ex)
                                    {
                                        if (!_cts.IsCancellationRequested && !_disposed)
                                            logger.LogError(ex, "[RobotUser] TransferEvent handler failed tx={Tx}", txHash);
                                    }
                                }

                                // Always try to raise TransactionEvent (dedup with VIP)
                                if (TransactionEvent != null)
                                {
                                    try
                                    {
                                        if (_eventFired.TryAdd(txHash, 0))
                                        {
                                            await TransactionEvent(txHash, resp.from, resp.gasPrice, resp.maxFeePerGas, resp.maxPriorityFeePerGas, resp.value, resp.input);
                                            Interlocked.Increment(ref _txEventFired);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (!_cts.IsCancellationRequested && !_disposed)
                                            logger.LogError(ex, "[RobotUser] TransactionEvent handler failed tx={Tx}", txHash);
                                    }
                                }
                            }
                            catch (RemoteInvocationException)
                            {
                                Interlocked.Increment(ref _rateLimited);
                                var next = Math.Min(Volatile.Read(ref _delayMs) + 50, _delayMaxMs);
                                Volatile.Write(ref _delayMs, next);
                                try { await Task.Delay(200, _cts.Token); } catch { break; }
                            }
                            catch (ConnectionLostException)
                            {
                                Interlocked.Increment(ref _connLost);
                                Volatile.Write(ref _connLostLastTx, txHash);
                            }
                            catch (OperationCanceledException) { break; }
                            catch (ObjectDisposedException) { break; }
                            catch (Exception ex)
                            {
                                if (!_cts.IsCancellationRequested && !_disposed)
                                    logger.LogError(ex, "[RobotUser] Pending worker error");
                            }
                        }
                    }, _cts.Token);
                }

                await Task.WhenAll(workers);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested && !_disposed)
                    logger.LogError(ex, "[RobotUser] EnsurePendingWorkerAsync error");
            }
            finally
            {
                _pendingWorkerGate.Release();
            }
        }

        // graceful stop
        public async Task StopAsync()
        {
            if (_disposed) return;
            try
            {
                _cts.Cancel();
                _statsCts.Cancel();
                try { _statsTimer?.Dispose(); } catch { /* ignore */ }
                // No StopListening() on RpcClient; cancellation is enough.
                await Task.Delay(100, CancellationToken.None);
            }
            catch { /* ignore */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts.Cancel(); } catch { }
            try { _statsCts.Cancel(); } catch { }
            try { _statsTimer?.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
            try { _statsCts.Dispose(); } catch { }
            try { _pendingWorkerGate?.Dispose(); } catch { }
        }
    }
}
