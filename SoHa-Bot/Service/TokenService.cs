
using Microsoft.Extensions.Logging;
using Soha.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Soha.Service
{
    public sealed class TokenService
    {
        private readonly ILogger logger;
        private readonly RpcClient rpcClient;
        private readonly Dictionary<string, string> name = new();
        private readonly Dictionary<string, decimal> decimals = new();
        public TokenService(RpcClient rpcClient, ILogger<TokenService> logger)
        {
            this.logger = logger;
            this.rpcClient = rpcClient;
        }
        public async Task<string> TokenNameAsync(string token)
        {
            if (name.TryGetValue(token, out string value))
            {
                return value;
            }
            string result = await rpcClient.GetNameAsync(token);
            name.Add(token, result);
            return result;
        }
        public async Task<decimal> TokenDecimalsAsync(string token)
        {
            if (decimals.TryGetValue(token, out decimal value))
            {
                return value;
            }
            decimal result = await rpcClient.GetDecimalsAsync(token);
            result = (decimal)Math.Pow(10, (double)result);
            decimals.Add(token, result);
            return result;
        }
    }
}
