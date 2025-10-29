using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Rsk.Util;
using Nethereum.Signer;
using Soha.Core;
using Soha.SwapFactory;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service
{
    public class AccountService
    {
        // Nonce lock to serialize signing/sending and avoid nonce collisions
        private readonly object locked = new object();

        private readonly ILogger logger;
        private readonly RpcClient rpcClient;

        private static readonly AddressUtil addressUtil = AddressUtil.Current;

        private EthECKey ethECKey;
        private BigInteger chainId;
        private string privateKey; // hex without 0x
        private string wallet;     // checksum address
        private int TransactionCount;

        public Dictionary<string, BigInteger> Approves = new();

        private readonly LegacyTransactionSigner legacyTransactionSigner = new LegacyTransactionSigner();

        public AccountService(ILogger<AccountService> logger, RpcClient rpcClient)
        {
            this.rpcClient = rpcClient;
            this.logger = logger;
        }

        /// <summary>
        /// Initialize account with wallet & private key.
        /// Stores fields correctly and fetches chainId and current nonce.
        /// </summary>
        public async Task<bool> RegisterAsync(string walletAddress, string privateKeyHex)
        {
            // 1) input checks (no throw)
            if (string.IsNullOrWhiteSpace(privateKeyHex))
            {
                logger.LogError("[AccountService] 私钥不能为空.");
                Environment.Exit(1);
            }

            var keyNoPrefix = privateKeyHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? privateKeyHex.Substring(2)
                : privateKeyHex;

            if (keyNoPrefix.Length != 64 || !Regex.IsMatch(keyNoPrefix, "^[0-9a-fA-F]+$"))
            {
                logger.LogError("[AccountService] 私钥格式不对");
                Environment.Exit(1);
            }

            try
            {
                // 2) initialize key & on-chain values
                ethECKey = new EthECKey(keyNoPrefix);
                privateKey = keyNoPrefix; // store without 0x
                chainId = await rpcClient.GetChainIdAsync();

                wallet = addressUtil.ConvertToChecksumAddress(walletAddress);
                TransactionCount = await rpcClient.GetTransactionCountAsync(wallet);

                logger.LogInformation("私钥初始化成功");
                logger.LogInformation("    Wallet: {Wallet}", wallet);
                return true;
            }
            catch (Exception ex)
            {
                // 3) swallow exception, log error, and return false (no stacktrace flood up the stack)
                logger.LogError(ex, "[AccountService] 私钥初始化失败");
                return false;
            }
        }

        /// <summary>
        /// Legacy: exact tokens -> tokens (single send, returns Task<string> hash task).
        /// </summary>
        public async Task<string> ExactTokensForTokensAsync(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasPrice,
            BigInteger gasCount)
        {
            return await Task.Run(() =>
            {
                Task<string> task;
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + 120));

                lock (locked)
                {
                    string encoded = legacyTransactionSigner.SignTransaction(
                        privateKey, chainId, swapFactory.SwapFactoryAddress, swapFactory.Amount,
                        TransactionCount, gasPrice, gasCount, data);

                    task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                    TransactionCount++;
                }

                return task;
            });
        }

        /// <summary>
        /// Legacy: exact tokens -> tokens (batched sends).
        /// </summary>
        public async Task<List<Task<string>>> ExactTokensForTokensAsync(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasPrice,
            BigInteger gasCount,
            int Repeat = 1,
            int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                var tasks = new List<Task<string>>(Repeat);
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));

                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        string encoded = legacyTransactionSigner.SignTransaction(
                            privateKey, chainId, swapFactory.SwapFactoryAddress, swapFactory.Amount,
                            TransactionCount, gasPrice, gasCount, data);

                        var tx = rpcClient.SendRawTransactionAsync("0x" + encoded);
                        tasks.Add(tx);
                        TransactionCount++;
                    }
                }

                return tasks;
            });
        }

        /// <summary>
        /// EIP-1559: exact tokens -> tokens (batched).
        /// </summary>
        public async Task<List<Task<string>>> ExactTokensForTokens1559Async(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasLimit,
            BigInteger? maxPriorityFeePerGas,
            BigInteger? maxFeePerGas,
            int Repeat = 1,
            int outTime = 1000)
        {
            return await Task.Run(async () =>
            {
                var tasks = new List<Task<string>>(Repeat);

                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(
                    amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));

                // fallback to legacy gasPrice if fees not provided
                if (!maxFeePerGas.HasValue || !maxPriorityFeePerGas.HasValue)
                {
                    var legacy = await rpcClient.GasPriceAsync();
                    if (!maxPriorityFeePerGas.HasValue) maxPriorityFeePerGas = legacy;
                    if (!maxFeePerGas.HasValue) maxFeePerGas = legacy;
                }

                var encodedList = new List<string>(Repeat);

                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        var tx1559 = new Transaction1559(
                            chainId,
                            TransactionCount,
                            maxPriorityFeePerGas,
                            maxFeePerGas,
                            gasLimit,
                            swapFactory.SwapFactoryAddress,
                            swapFactory.Amount,
                            data,
                            null);

                        tx1559.Sign(ethECKey);
                        encodedList.Add(tx1559.GetRLPEncoded().ToHex(true)); // includes 0x
                        TransactionCount++;
                    }
                }

                foreach (var enc in encodedList)
                {
                    tasks.Add(rpcClient.SendRawTransactionAsync(enc));
                }

                return tasks;
            });
        }

        /// <summary>
        /// Legacy: exact ETH -> tokens (single send).
        /// </summary>
        public async Task<string> ExactETHForTokensAsync(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasPrice,
            BigInteger gasCount)
        {
            return await Task.Run(() =>
            {
                Task<string> task;
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(amountOutMin, path, wallet, new BigInteger(unixTimestamp + 120));

                lock (locked)
                {
                    string encoded = legacyTransactionSigner.SignTransaction(
                        privateKey, chainId, swapFactory.SwapFactoryAddress, amountIn,
                        TransactionCount, gasPrice, gasCount, data);

                    task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                    TransactionCount++;
                }

                return task;
            });
        }

        /// <summary>
        /// Legacy: exact ETH -> tokens (batched).
        /// </summary>
        public async Task<List<Task<string>>> ExactETHForTokensAsync(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasPrice,
            BigInteger gasCount,
            int Repeat = 1,
            int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                var tasks = new List<Task<string>>(Repeat);
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));

                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        string encoded = legacyTransactionSigner.SignTransaction(
                            privateKey, chainId, swapFactory.SwapFactoryAddress, amountIn,
                            TransactionCount, gasPrice, gasCount, data);

                        var tx = rpcClient.SendRawTransactionAsync("0x" + encoded);
                        tasks.Add(tx);
                        TransactionCount++;
                    }
                }

                return tasks;
            });
        }

        /// <summary>
        /// EIP-1559: exact ETH -> tokens (batched).
        /// </summary>
        public async Task<List<Task<string>>> ExactETHForTokens1559Async(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasLimit,
            BigInteger? maxPriorityFeePerGas,
            BigInteger? maxFeePerGas,
            int Repeat = 1,
            int outTime = 1000)
        {
            return await Task.Run(async () =>
            {
                var tasks = new List<Task<string>>(Repeat);

                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(
                    amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));

                // fallback if fees are missing
                if (!maxFeePerGas.HasValue || !maxPriorityFeePerGas.HasValue)
                {
                    var legacy = await rpcClient.GasPriceAsync();
                    if (!maxPriorityFeePerGas.HasValue) maxPriorityFeePerGas = legacy;
                    if (!maxFeePerGas.HasValue) maxFeePerGas = legacy;
                }

                var encodedList = new List<string>(Repeat);

                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        var tx1559 = new Transaction1559(
                            chainId,
                            TransactionCount,
                            maxPriorityFeePerGas,
                            maxFeePerGas,
                            gasLimit,
                            swapFactory.SwapFactoryAddress,
                            amountIn, // value in wei
                            data,
                            null);

                        tx1559.Sign(ethECKey);
                        encodedList.Add(tx1559.GetRLPEncoded().ToHex(true));
                        TransactionCount++;
                    }
                }

                foreach (var enc in encodedList)
                {
                    tasks.Add(rpcClient.SendRawTransactionAsync(enc));
                }

                return tasks;
            });
        }

        /// <summary>
        /// Approve spender on an ERC20.
        /// </summary>
        public async Task ApproveAsync(string Contract, string Spender, BigInteger Value, BigInteger? gasPrice = null)
        {
            if (!gasPrice.HasValue)
            {
                gasPrice = await rpcClient.GasPriceAsync();
            }

            Task<string> task;
            lock (locked)
            {
                if (!Approves.ContainsKey(Contract))
                    Approves.Add(Contract, Value);

                string encoded = legacyTransactionSigner.SignTransaction(
                    privateKey, chainId, Contract, 0, TransactionCount, gasPrice.Value, 800000,
                    $"0x095ea7b3{Spender.Remove(0, 2).PadLeft(64, '0')}ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

                task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                TransactionCount++;
            }

            logger.LogInformation("[Approve] Tx: {Hash}", await task);
        }

        /// <summary>
        /// Legacy: invoke custom data with fixed gas.
        /// </summary>
        public Task<string> InvokeAsync(string Contract, BigInteger gasPrice, BigInteger gasCount, string Data)
        {
            Task<string> invokeTask;

            lock (locked)
            {
                string encoded = legacyTransactionSigner.SignTransaction(
                    privateKey, chainId, Contract, 0, TransactionCount, gasPrice, gasCount, Data);

                invokeTask = rpcClient.SendRawTransactionAsync("0x" + encoded);
                TransactionCount++;
            }

            return invokeTask;
        }

        /// <summary>
        /// Legacy: invoke custom data with auto gas price and default gas limit=800000.
        /// </summary>
        public async Task InvokeAsync(string Contract, string Data)
        {
            BigInteger GasPrice = await rpcClient.GasPriceAsync();

            Task<string> txTask;
            lock (locked)
            {
                string encoded = legacyTransactionSigner.SignTransaction(
                    privateKey, chainId, Contract, 0, TransactionCount, GasPrice, 800000, Data);

                txTask = rpcClient.SendRawTransactionAsync("0x" + encoded);
                TransactionCount++;
            }

            string tx = await txTask;
            logger.LogInformation("[Invoke] Tx: {Hash}", tx);
        }

        /// <summary>
        /// Sell token for token (legacy), with optional block/time delay.
        /// </summary>
        /// <summary>
        /// EIP-1559: invoke custom calldata (type-2 tx).
        /// Follows maxPriorityFeePerGas / maxFeePerGas from the triggering tx.
        /// </summary>
        public Task<string> Invoke1559Async(
            string contract,
            BigInteger gasLimit,
            BigInteger maxPriorityFeePerGas,
            BigInteger maxFeePerGas,
            string data)
        {
            lock (locked)
            {
                var tx1559 = new Transaction1559(
                    chainId,                 // chainId
                    TransactionCount,        // nonce
                    maxPriorityFeePerGas,    // maxPriorityFeePerGas
                    maxFeePerGas,            // maxFeePerGas
                    gasLimit,                // gasLimit
                    contract,                // recipient (use positional arg; don't name it "to")
                    0,                       // value
                    data,                    // data
                    null                     // accessList
                );

                tx1559.Sign(ethECKey);
                var raw = tx1559.GetRLPEncoded().ToHex(true); // includes "0x"
                TransactionCount++;
                return rpcClient.SendRawTransactionAsync(raw);
            }
        }

        public async Task SellTokensForTokensAsync(
            ISwapFactory swapFactory,
            BigInteger amountIn,
            BigInteger amountOutMin,
            string[] path,
            BigInteger gasPrice,
            BigInteger gasCount,
            ulong? sellBlock,
            ulong? sellTime)
        {
            if (sellTime.HasValue && sellTime.Value > 0)
            {
                Thread.Sleep((int)(sellTime.Value));
            }

            if (sellBlock.HasValue && sellBlock > 0)
            {
                while (true)
                {
                    ulong blockNumber = await rpcClient.BlockNumberAsync();
                    if (blockNumber >= sellBlock) break;
                    Thread.Sleep((int)((sellBlock - blockNumber) * 800));
                }
            }

            string result = await ExactTokensForTokensAsync(
                swapFactory, amountIn, amountOutMin, path, gasPrice, gasCount);

            logger.LogInformation("[Sell]");
            logger.LogInformation("    Amount: {Amount} MinOut: {MinOut} Path: {Path}",
                amountIn, amountOutMin, string.Join(" => ", path));
            logger.LogInformation("    Sent Tx: {Hash}", result);
        }

        public async Task<BigInteger> BalanceOfAsync(string Contract)
        {
            return await rpcClient.BalanceOfAsync(Contract, wallet);
        }
    }
}
