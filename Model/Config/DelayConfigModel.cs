namespace Soha.Model.Config
{
    public sealed class DelayConfigModel
    {
        // OLD: Interval (ms) -- deprecated
        public int? Interval { get; set; }
        // NEW
        public int? TimeDelay { get; set; }   // ms, between rounds
        public int? BlockDelay { get; set; }  // blocks, optional
        // BACK-COMPAT (optional): keep Interval to not break old YAML
    }
}