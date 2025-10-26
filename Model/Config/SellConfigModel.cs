namespace Soha.Model.Config
{
    public sealed class SellConfigModel
    {
        public bool Enabled { get; set; }
#nullable enable
        public int? GasPrice { get; set; }
        public string? Receive { get; set; }
        public byte? Percentage { get; set; } = 100;
        public decimal? AmountOutMin { get; set; } = 0;
#nullable disable
        public DelayConfigModel Delay { get; set; }
    }
}
