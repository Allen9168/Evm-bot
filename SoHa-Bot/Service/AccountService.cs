using Microsoft.Extensions.Logging;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Rsk.Util;
using Nethereum.Signer;
using Soha.Core;
using Soha.SwapFactory;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Soha.Service
{
    public class AccountService
    {
        private readonly object locked = new object();

        private readonly ILogger logger;
        private readonly RpcClient rpcClient;

        private static readonly AddressUtil addressUtil = AddressUtil.Current;

        private EthECKey ethECKey;
        private BigInteger chainId;
        private string privateKey;
        private string wallet;
        private int TransactionCount;



        public Dictionary<string, BigInteger> Approves = new();

        private readonly LegacyTransactionSigner legacyTransactionSigner = new LegacyTransactionSigner();
        public AccountService(ILogger<AccountService> logger, RpcClient rpcClient)
        {
            this.rpcClient = rpcClient;
            this.logger = logger;
        }
        public async Task RegisterAsync(string Wallet, string PrivateKey)
        {
            ethECKey = new EthECKey(PrivateKey);
            chainId = await rpcClient.GetChainIdAsync();
            wallet = addressUtil.ConvertToChecksumAddress(Wallet);
            privateKey = PrivateKey;
            TransactionCount = await rpcClient.GetTransactionCountAsync(wallet);
        }
        public async Task<string> ExactTokensForTokensAsync(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasPrice, BigInteger gasCount)
        {
            return await Task.Run(() =>
            {
                Task<string> task;
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + 120));
                lock (locked)
                {
                    string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, swapFactory.SwapFactoryAddress, swapFactory.Amount, TransactionCount, gasPrice, gasCount, data);
                    task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                    TransactionCount++;
                }
                return task;
            });
        }
        public async Task<List<Task<string>>> ExactTokensForTokensAsync(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasPrice, BigInteger gasCount, int Repeat = 1, int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                List<Task<string>> tasks = new List<Task<string>>();
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));
                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, swapFactory.SwapFactoryAddress, swapFactory.Amount, TransactionCount, gasPrice, gasCount, data);
                        Task<string> transactionTaask = rpcClient.SendRawTransactionAsync("0x" + encoded);
                        tasks.Add(transactionTaask);
                        TransactionCount++;
                    }
                }
                return tasks;
            });
        }
        public async Task<List<Task<string>>> ExactTokensForTokens1559Async(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasLimit, BigInteger? maxPriorityFeePerGas, BigInteger? maxFeePerGas, int Repeat = 1, int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                List<Task<string>> tasks = new List<Task<string>>();
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactTokensForTokens(amountIn, amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));
                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        Transaction1559 transaction1559 = new Transaction1559(chainId, TransactionCount, maxPriorityFeePerGas, maxFeePerGas, gasLimit, swapFactory.SwapFactoryAddress, swapFactory.Amount, data, null);
                        transaction1559.Sign(ethECKey);
                        string encoded = transaction1559.GetRLPEncoded().ToHex(true);
                        Task<string> transactionTaask = rpcClient.SendRawTransactionAsync(encoded);
                        tasks.Add(transactionTaask);
                        TransactionCount++;
                    }
                }
                return tasks;
            });
        }
        public async Task<string> ExactETHForTokensAsync(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasPrice, BigInteger gasCount)
        {
            return await Task.Run(() =>
            {
                Task<string> task;
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(amountOutMin, path, wallet, new BigInteger(unixTimestamp + 120));
                lock (locked)
                {
                    string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, swapFactory.SwapFactoryAddress, amountIn, TransactionCount, gasPrice, gasCount, data);
                    task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                    TransactionCount++;
                }
                return task;
            });
        }
        public async Task<List<Task<string>>> ExactETHForTokensAsync(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasPrice, BigInteger gasCount, int Repeat = 1, int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                List<Task<string>> tasks = new List<Task<string>>();
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));
                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, swapFactory.SwapFactoryAddress, amountIn, TransactionCount, gasPrice, gasCount, data);
                        Task<string> transactionTaask = rpcClient.SendRawTransactionAsync("0x" + encoded);
                        tasks.Add(transactionTaask);
                        TransactionCount++;
                    }
                }
                return tasks;
            });
        }
        public async Task<List<Task<string>>> ExactETHForTokens1559Async(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasLimit, BigInteger? maxPriorityFeePerGas, BigInteger? maxFeePerGas, int Repeat = 1, int outTime = 1000)
        {
            return await Task.Run(() =>
            {
                List<Task<string>> tasks = new List<Task<string>>();
                int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string data = swapFactory.SwapExactETHForTokens(amountOutMin, path, wallet, new BigInteger(unixTimestamp + outTime));
                lock (locked)
                {
                    for (int i = 0; i < Repeat; i++)
                    {
                        Transaction1559 transaction1559 = new Transaction1559(chainId, TransactionCount, maxPriorityFeePerGas, maxFeePerGas, gasLimit, swapFactory.SwapFactoryAddress, amountIn, data, null);
                        transaction1559.Sign(ethECKey);
                        string encoded = transaction1559.GetRLPEncoded().ToHex(true);
                        Task<string> transactionTaask = rpcClient.SendRawTransactionAsync(encoded);
                        tasks.Add(transactionTaask);
                        TransactionCount++;
                    }
                }
                return tasks;
            });
        }
        public async Task ApproveAsync(string Contract, string Spender, BigInteger Value, BigInteger? gasPrice = null)
        {
            if (!gasPrice.HasValue)
            {
                gasPrice = await rpcClient.GasPriceAsync();
            }
            Task<string> task;
            lock (locked)
            {
                Approves.Add(Contract, Value);
                string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, Contract, 0, TransactionCount, gasPrice.Value, 800000, $"0x095ea7b3{Spender.Remove(0, 2).PadLeft(64, '0')}ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
                task = rpcClient.SendRawTransactionAsync("0x" + encoded);
                TransactionCount++;
            }
            logger.LogInformation($"    [Approve]  Tx : {await task}");
        }
        public Task<string> InvokeAsync(string Contract, BigInteger gasPrice, BigInteger gasCount, string Data)
        {
            Task<string> invokeTask;
            string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, Contract, 0, TransactionCount, gasPrice, gasCount, Data);
            lock (locked)
            {
                //try
                //{
                //    var tx = rpcClient.SendRawTransactionAsync("0x" + encoded).GetAwaiter().GetResult();
                //}
                //catch (Exception ex)
                //{
                //    logger.LogError(ex.Message);
                //}
                invokeTask = rpcClient.SendRawTransactionAsync("0x" + encoded);
                TransactionCount++;
            }
            return invokeTask;
        }
        public async Task InvokeAsync(string Contract, string Data)
        {
            BigInteger GasPrice = await rpcClient.GasPriceAsync();
            string encoded = legacyTransactionSigner.SignTransaction(privateKey, chainId, Contract, 0, TransactionCount, GasPrice, 800000, Data);
            Task<string> transactionTaask = rpcClient.SendRawTransactionAsync("0x" + encoded);
            await Task.WhenAll(transactionTaask);
            logger.LogInformation($"    [Invoke]  Tx : {await transactionTaask}");
            TransactionCount++;
        }
        public async Task SellTokensForTokensAsync(ISwapFactory swapFactory, BigInteger amountIn, BigInteger amountOutMin, string[] path, BigInteger gasPrice, BigInteger gasCount, ulong? sellBlock, ulong? sellTime)
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
                    if (blockNumber >= sellBlock)
                    {
                        break;
                    }
                    Thread.Sleep((int)((sellBlock - blockNumber) * 800));
                }
            }
            string result = await ExactTokensForTokensAsync(swapFactory, amountIn, amountOutMin, path, gasPrice, gasCount);
            logger.LogInformation($"[Sell]");
            logger.LogInformation($"     数量 : {amountIn} 滑点 : {amountOutMin} 路径 : {string.Join(" => ", path)}");
            logger.LogInformation($"     Tx   : {result} ");
        }
        public async Task<BigInteger> BalanceOfAsync(string Contract)
        {
            return await rpcClient.BalanceOfAsync(Contract, wallet);
        }
    }
}