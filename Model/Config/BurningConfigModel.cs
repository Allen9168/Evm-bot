namespace Soha.Model.Config
{
    public sealed class BurningConfigModel
    {
        public uint? StartTime { get; set; }
        public uint? StartBlock { get; set; }
        public ulong Duration { get; set; }
        public string Action { get; set; }
        public DelayConfigModel Delay { get; set; }
#nullable enable
        public InvokeConfigModel? Invoke { get; set; }
        public SwapConfigModel? Swap { get; set; }
#nullable disable
    }
}
