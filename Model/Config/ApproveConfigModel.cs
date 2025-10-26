namespace Soha.Model.Config
{
    public sealed class ApproveConfigModel
    {
        public bool Enabled { get; set; }
#nullable enable
        public string? Spender { get; set; }
        public string? Value { get; set; }
#nullable disable
    }
}