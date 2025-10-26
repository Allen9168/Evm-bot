using System.Collections.Generic;

namespace Soha.Model.Config
{
    public sealed class SwapConfigModel
    {
        public decimal AmountIn { get; set; }
        public decimal AmountOutMin { get; set; }
        public string[] Path { get; set; }
        public List<ulong> GasPrice { get; set; }
        public ulong? GasCount { get; set; }
    }
}
