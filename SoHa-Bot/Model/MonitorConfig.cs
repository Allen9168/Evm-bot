using System.Collections.Generic;

namespace Soha.Model
{
    public class MonitorConfig
    {
        public bool Enabled { get; set; }
        public List<TokenMonitor> Tokens { get; set; }
    }
    public class TokenMonitor
    {
        public int Amount { get; set; }
        public string Contract { get; set; }
    }
}
