namespace Soha.Model.Event
{
    public sealed class NewHeads
    {
        public string difficulty { get; set; }
        public string extraData { get; set; }
        public string gasLimit { get; set; }
        public string gasUsed { get; set; }
        public string logsBloom { get; set; }
        public string miner { get; set; }
        public string nonce { get; set; }
        public string number { get; set; }
        public string parentHash { get; set; }
        public string receiptRootreceiptRoot { get; set; }
        public string sha3Uncles { get; set; }
        public string stateRoot { get; set; }
        public string timestamp { get; set; }
        public string transactionsRoot { get; set; }
    }
}
