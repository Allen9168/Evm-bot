namespace Soha.Model.Config
{
    public sealed class InvokeConfigModel
    {
        public string MethodID { get; set; }
        public ulong? GasPrice { get; set; }
        public ulong? GasCount { get; set; }
#nullable enable
        public string? Data { get; set; }
#nullable disable
    }
}