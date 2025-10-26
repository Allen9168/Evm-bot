using System.Collections.Generic;

namespace Soha.Model.Config
{
    public sealed class LiquidityConfigModel
    {
#nullable enable
        public LiquidityConfig? Liquidity { get; set; }
        public LiquidityETHConfig? LiquidityETH { get; set; }
#nullable disable
        public class LiquidityConfig
        {
            public bool Enabled { get; set; }
            public List<string> Token { get; set; }
            public int Amount { get; set; }
        }
        public class LiquidityETHConfig
        {
            public bool Enabled { get; set; }
            public int Amount { get; set; }
        }
    }
}