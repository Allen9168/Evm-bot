using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoHa_Bot.Model;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Soha.Service
{
    public sealed class AccountManager
    {
        public readonly ILogger logger;
        public readonly ILifetimeScope lifetimeScope;
        public readonly Dictionary<string, List<AccountService>> Accounts = new Dictionary<string, List<AccountService>>();
        public AccountManager(ILifetimeScope lifetimeScope, ILogger<AccountManager> logger)
        {
            this.logger = logger;
            this.lifetimeScope = lifetimeScope;
        }
        public async Task LoadAccountAsync()
        {
            string Current = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Account");
            logger.LogInformation($"[加载地址]");
            foreach (string accountPath in Directory.GetFiles(Current, "*.yaml"))
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddYamlFile(accountPath)
                    .Build();

                AccountConfig accountConfig = configuration.Get<AccountConfig>();
                AccountService accountService = lifetimeScope.Resolve<AccountService>();
                await accountService.RegisterAsync(accountConfig.Wallet, accountConfig.PrivateKey);

                string groupList = string.Empty;
                for (int i = 0; i < accountConfig.Groups.Count; i++)
                {
                    string group = accountConfig.Groups[i];
                    if (i == 0)
                    {
                        groupList = string.Concat(groupList, group);
                    }
                    else
                    {
                        groupList = string.Concat(groupList, ",", group);
                    }
                }
                logger.LogInformation($"    钱包地址 : {accountConfig.Wallet} 分组 : {groupList}");
                // accountService.InvokeAsync("0xc1d0e4dc98a1d5b64c38e8f3d44843ba24a109c0", "0xd96a094a000000000000000000000000000000000000000000000000000000000000000a").GetAwaiter().GetResult(); 
                // BigInteger amountIn = new BigInteger(1000000000000000000u) / 100;
                // BigInteger bigInteger = BigInteger.Parse("0ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber);
                // accountService.ApproveAsync("0x10ed43c718714eb63d5aa57b78b54704e256024e", "0x373e768f79c820aa441540d254dca6d045c6d25b", bigInteger).GetAwaiter().GetResult();
                // accountService.ExactETHForTokensAsync(new PancakeSwap(), 100000000000000, new string[] { "0xbb4cdb9cbd36b01bd1cbaebf2de08d9173bc095c", "0x82cd1fbd2be2580e925b2d3f4be01df997ed0468" }, 1000000000, 5000000).GetAwaiter().GetResult();
                //  accountService.ExactTokensForTokensAsync(new PancakeSwap(), 0, 0, new string[] { "0xbb4cdb9cbd36b01bd1cbaebf2de08d9173bc095c", "0x82cd1fbd2be2580e925b2d3f4be01df997ed0468" }, 1000000000, 5000000).GetAwaiter().GetResult();
                // int unixTimestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                // accountService.SwapExactTokensForTokensSupportingFeeOnTransferTokensAsync(new PandaexSwap(), "0xc3364A27f56b95f4bEB0742a7325D67a04D80942", 100000000, Count, 0, new string[] { "0x382bB369d343125BfB2117af9c149795C6C65C50", "0xab0d1578216a545532882e420a8c61ea07b00b12" }, unixTimestamp + 1000).GetAwaiter().GetResult();
                foreach (string Group in accountConfig.Groups)
                {
                    if (Accounts.TryGetValue(Group, out List<AccountService> accounts))
                    {
                        accounts.Add(accountService);
                    }
                    else
                    {
                        accounts = new List<AccountService>() { accountService };
                        Accounts.Add(Group, accounts);
                    }
                }
            }
        }

        public List<AccountService> GetAccount(string Group)
        {
            if (Accounts.TryGetValue(Group, out List<AccountService> accounts))
            {
                return accounts;
            }
            return new List<AccountService>();
        }
    }
}
