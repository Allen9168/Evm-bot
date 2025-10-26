using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using StreamJsonRpc;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Soha.Core
{
    public class RpcClient : JsonRpc
    {
        private readonly ILogger logger;
        public RpcClient(ILogger<RpcClient> logger, MessageHandlerBase messageHandlerBase) : base(messageHandlerBase)
        {
            this.logger = logger;
        }
        public async Task<string> SubscribeAsync(string Heads)
        {
            logger.LogDebug($"Method : 请求查询类型 : {Heads}");
            string response = await InvokeAsync<string>("eth_subscribe", new string[] { Heads });
            logger.LogDebug($"     成功获得ID : {response}");
            return response;
        }
        public async Task<bool> UnSubscribeAsync(string Subscription_Id)
        {
            logger.LogDebug($"Method : UnSubscribe Addres : {Subscription_Id}");
            bool response = await InvokeAsync<bool>("eth_unsubscribe", new string[] { Subscription_Id });
            logger.LogDebug($"     Result : {response}");
            return response;
        }

        public async Task<BigInteger> GetChainIdAsync()
        {
            logger.LogDebug($"检测网络ID");

            string response = await InvokeAsync<string>("eth_chainId");

            // ✅ 获取网络名称
            string networkName = GetNetworkName(response);

            // ✅ 输出格式化日志
            logger.LogDebug($"当前网络为: {response} ({networkName})");

            return BigInteger.Parse($"0{response.Remove(0, 2)}", NumberStyles.HexNumber);
        }

        private static string GetNetworkName(string chainIdHex)
        {
            if (chainIdHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                chainIdHex = chainIdHex.Substring(2);

            if (!ulong.TryParse(chainIdHex, NumberStyles.HexNumber, null, out ulong chainId))
                return $"未知网络 ({chainIdHex})";

            return chainId switch
            {
                1 => "Ethereum 主网",
                3 => "Ropsten 测试网",
                4 => "Rinkeby 测试网",
                5 => "Goerli 测试网",
                56 => "BSC 主网",
                97 => "BSC 测试网",
                137 => "Polygon 主网",
                80001 => "Polygon Mumbai 测试网",
                42161 => "Arbitrum One",
                421611 => "Arbitrum Rinkeby",
                10 => "Optimism 主网",
                11155111 => "Sepolia 测试网",
                43114 => "Avalanche 主网",
                43113 => "Avalanche Fuji 测试网",
                250 => "Fantom 主网",
                4002 => "Fantom 测试网",
                _ => $"未知网络 ({chainId})"
            };
        }

        public async Task<string> GetBalanceAsync(string Addres, string QuanTity)
        {
            logger.LogDebug($"Method : GetBalance Addres : {Addres} QuanTity : {QuanTity}");
            string response = await InvokeAsync<string>("eth_getBalance", new string[] { Addres, QuanTity });
            logger.LogDebug($"     Result : {response}");
            return response;
        }

        /// <summary>
        /// 返回指定地址发生的交易数量
        /// </summary>
        /// <param name="Addres">地址</param>
        /// <param name="Quantity">整数块编号，或字符串"latest","earliest"或"pending"</param>
        /// <returns>从指定地址发出的交易数量，整数</returns>
        public async Task<int> GetTransactionCountAsync(string Addres, string Quantity = "latest")
        {
            // logger.LogDebug($"[GetTransactionCount]  Addres : {Addres} Quantity : {Quantity} ");
            string response = await InvokeAsync<string>("eth_getTransactionCount", new object[] { Addres, Quantity });
            // logger.LogDebug($"Result : {response}");
            return Convert.ToInt32(response, 16);
        }
        /// <summary>
        /// 为签名交易创建一个新的消息调用交易或合约
        /// </summary>
        /// <param name="Transaction">签名的交易数据</param>
        /// <returns>交易哈希，如果交易未生效则返回全0哈希</returns>
        public async Task<string> SendRawTransactionAsync(string Transaction)
        {
            string response = string.Empty;
            try
            {
                // logger.LogDebug($"[SendRawTransaction]  Transaction : {Transaction}");
                response = await InvokeAsync<string>("eth_sendRawTransaction", new object[] { Transaction });
                // logger.LogDebug($"Result : {response}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return response;
        }
        /// <summary>
        /// 返回指定地址的代码
        /// </summary>
        /// <param name="Addres">地址</param>
        /// <param name="Quantity">整数块编号，或字符串"latest"、"earliest" 或"pending"</param>
        /// <returns></returns>
        public async Task<string> GetCodeAsync(string Addres, string Quantity = "latest")
        {
            logger.LogDebug($"[GetCode]  Addres : {Addres} Quantity : {Quantity}");
            string response = await InvokeAsync<string>("eth_getCode", new object[] { Addres, Quantity });
            logger.LogDebug($"    Result : {response}");
            return response;
        }
        /// <summary>
        /// 返回当前的gas价格，单位:wei
        /// </summary>
        /// <returns></returns>
        public async Task<BigInteger> GasPriceAsync()
        {
            logger.LogDebug($"[GasPrice]");
            string response = await InvokeAsync<string>("eth_gasPrice", new object[] { });
            logger.LogDebug($"    Result : {response}");
            string str = response.Remove(0, 2);
            return BigInteger.Parse($"0{str}", NumberStyles.HexNumber);
        }
        /// <summary>
        /// 返回最新块的编号
        /// </summary>
        /// <returns>节点当前块编号</returns>
        public async Task<ulong> BlockNumberAsync()
        {
            // logger.LogDebug($"[BlockNumber]");
            string response = await InvokeAsync<string>("eth_blockNumber", new object[] { });
            return Convert.ToUInt64(response, 16);
        }
        /// <summary>
        /// 执行并估算一个交易需要的gas用量
        /// </summary>
        /// <param name="Addres"></param>
        /// <returns></returns>
        public async Task<ulong> EstimateGasAsync(string Addres)
        {
            logger.LogDebug($"[EstimateGas]");
            string response = await InvokeAsync<string>("eth_estimateGas", new object[] { new { from = Addres } });
            logger.LogDebug($"    Result : {response}");
            return Convert.ToUInt64(response, 16);
        }
        public async Task<string> GetNameAsync(string Token, string Quantity = "latest")
        {
            // logger.LogDebug($"[GetDecimals]");
            string response = await InvokeAsync<string>("eth_call", new object[] { new { data = "0x06fdde03", to = Token }, Quantity });
            // logger.LogDebug($"    Result : {response}");
            response = response.Remove(0, 2);
            int len = response.Length / 2;

            List<byte> buffer = new List<byte>();
            for (int i = 0; i < len; i++)
            {
                byte b = byte.Parse(response.Substring(2 * i, 2), NumberStyles.HexNumber);
                if (b == 0)
                {
                    continue;
                }
                buffer.Add(b);
            }
            response = Encoding.UTF8.GetString(buffer.ToArray()).Trim();
            return response;
        }
        public async Task<ushort> GetDecimalsAsync(string Token, string Quantity = "latest")
        {
            // logger.LogDebug($"[GetDecimals]");
            string response = await InvokeAsync<string>("eth_call", new object[] { new { data = "0x313ce567", to = Token }, Quantity });
            response = response.Substring(response.Length - 8, 8);
            // logger.LogDebug($"    Result : {response}");
            return ushort.Parse(response, NumberStyles.HexNumber);
        }
        /// <summary>
        /// 返回指定编号的块
        /// </summary>
        /// <param name="BlockId">整数块编号，或字符串"earliest"、"latest" 或"pending"</param>
        /// <param name="flag">为true时返回完整的交易对象，否则仅返回交易哈希</param>
        /// <returns></returns>
        public async Task<string> GetBlockByNumberAsync(string BlockId, string flag)
        {
            logger.LogDebug($"[GetBlockByNumber] BlockId : {BlockId} string : {flag}");
            string response = await InvokeAsync<string>("eth_getCode", new object[] { BlockId, flag });
            logger.LogDebug($"    Result : {response}");
            return response;
        }
        public static string GetTransactionHash(string rawTransaction)
        {
            int offset = rawTransaction.StartsWith("0x") ? 2 : 0;

            byte[] txByte = Enumerable.Range(offset, rawTransaction.Length - offset)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(rawTransaction.Substring(x, 2), 16))
                             .ToArray();

            //Note: Not intended for intensive use so we create a new Digest.
            //if digest reuse, prevent concurrent access + call Reset before BlockUpdate
            KeccakDigest digest = new KeccakDigest(256);

            digest.BlockUpdate(txByte, 0, txByte.Length);
            byte[] calculatedHash = new byte[digest.GetByteLength()];
            digest.DoFinal(calculatedHash, 0);

            string transactionHash = BitConverter.ToString(calculatedHash, 0, 32).Replace("-", "").ToLower();

            return transactionHash;
        }
        public async Task<BigInteger> BalanceOfAsync(string Contract, string Wallet)
        {
            string response = await InvokeAsync<string>("eth_call", new object[] { new { data = $"0x70a08231000000000000000000000000{Wallet.Remove(0, 2)}", from = "0x0000000000000000000000000000000000000000", to = Contract }, "latest" });
            return BigInteger.Parse($"0{response.Remove(0, 2)}", NumberStyles.HexNumber);
        }
    }
}