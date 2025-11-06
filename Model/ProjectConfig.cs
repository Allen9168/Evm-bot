using Soha.Model.Config;
using System.Collections.Generic;
using Soha.Model;

namespace SoHa_Bot.Model
{
    public sealed class ProjectConfig
    {
        public bool Enabled { get; set; }
        public string Name { get; set; }
#nullable enable
        public string? SwapRouter { get; set; }
        public string? Contract { get; set; }
#nullable disable
        public List<string> AccountGroups { get; set; }
        public string Model { get; set; }
        public TransactionConfigModel Transaction { get; set; }
        public ExactConfigModel Exact { get; set; }
#nullable enable
        public DelayConfigModel? Delay { get; set; }
        public LiquidityConfigModel? Liquiditys { get; set; }
        public InvokeConfigModel? Invoke { get; set; }
        public BurningConfigModel? Burning { get; set; }
        public SellConfigModel? Sell { get; set; }
        public ApproveConfigModel? Approve { get; set; }
        public string? Sender { get;  set; }
        public MonitorConfig? Monitor { get; set; }
#nullable disable
    }
}
