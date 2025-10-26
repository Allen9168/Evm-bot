using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soha.Core.Response.Eth;
using Soha.Model;
using System.Globalization;
using System.Numerics;

namespace Soha.Service
{
    public sealed class TransactionsService
    {
        private readonly ILogger logger;

        private int ETHValue;
        private MonitorConfig MonitorConfig;
        public TransactionsService(IConfiguration configuration, ILogger<TransactionsService> logger)
        {
            this.logger = logger;
            ushort Value = configuration.GetValue<ushort>("Transcation:Value");
            MonitorConfig = configuration.GetSection("Monitor").Get<MonitorConfig>();
            foreach (TokenMonitor token in MonitorConfig.Tokens)
            {
                if (token.Contract.Equals("0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c"))
                {
                    ETHValue = token.Amount;
                }
            }
        }
        public void NewTransactions(TransactionResponse response, string result, BigInteger value)
        {
            foreach (TokenMonitor token in MonitorConfig.Tokens)
            {
                if (response.to.Equals(token.Contract))
                {
                    string recipient = response.input.Substring(34, 40);
                    string amount = response.input.Substring(74, 64);
                    value = BigInteger.Parse(amount, NumberStyles.HexNumber);
                    value /= 1000000000000000000;
                    if (value > token.Amount)
                    {
                        logger.LogInformation($"[{TokenName(response.to)}] Tx : {result} form : {response.from} to : 0x{recipient}  amount : {value}");
                    }
                    return;
                }
            }
            if (value >= ETHValue)
            {
                logger.LogInformation($"[BNB] Tx : {result}  form : {response.from} to : {response.to} Value : {value}");
            }
        }
        public string TokenName(string Contract)
        {
            switch (Contract)
            {
                case "0x55d398326f99059ff775485246999027b3197955": return "BNB";
                case "0xe9e7cea3dedca5984780bafc599bd69add087d56": return "BUSD";
                case "0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c": return "WBNB";
            }
            return string.Empty;
        }
    }
}