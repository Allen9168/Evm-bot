namespace Soha.Model.Config
{
    public sealed class ExactConfigModel
    {
        public decimal? AmountOutMin { get; set; }
#nullable enable
        public string? Send { get; set; }
        public string? OutTime { get; set; }
#nullable disable
        public decimal? GasPrice { get; set; }
        public decimal? GasCount { get; set; }
    }
}