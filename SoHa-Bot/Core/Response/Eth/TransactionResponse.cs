using System.Collections.Generic;

namespace Soha.Core.Response.Eth
{
    public class TransactionResponse
    {
        public string blockHash { get; set; }
        public string blockNumber { get; set; }
        public string from { get; set; }
        public string gas { get; set; }
        public string gasPrice { get; set; }
#nullable enable
        public string? maxFeePerGas { get; set; }
        public string? maxPriorityFeePerGas { get; set; }
#nullable disable
        public string hash { get; set; }
        public string input { get; set; }
        public string nonce { get; set; }
        public string to { get; set; }
        public string transactionIndex { get; set; }
        public string value { get; set; }
        public string type { get; set; }
#nullable enable
        public List<string>? accessList { get; set; }
        public string? chainId { get; set; }
#nullable disable
    }
}
