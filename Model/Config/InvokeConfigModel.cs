namespace Soha.Model.Config
{
    public sealed class InvokeConfigModel
    {
        public string MethodID { get; set; }
        public ulong? GasPrice { get; set; }
        public ulong? GasCount { get; set; }
#nullable enable
        public string? Data { get; set; }
        public bool? Use1559 { get; set; }                 // true: use EIP-1559 below
        public decimal? MaxPriorityFeePerGas { get; set; } // gwei
        public decimal? MaxFeePerGas { get; set; }         // gwei
        public ulong? GasLimit { get; set; }               // gas units

        // ---- Legacy (backward-compatible) ----

#nullable disable
    }
}