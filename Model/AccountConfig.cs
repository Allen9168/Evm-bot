using System.Collections.Generic;

namespace SoHa_Bot.Model
{
    public class AccountConfig
    {
        public string Wallet { get; set; }
        public string PrivateKey { get; set; }
        public List<string> Groups { get; set; }
    }
}
