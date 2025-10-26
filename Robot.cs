using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soha.Service;
using System.Threading;
using System.Threading.Tasks;

namespace Soha
{
    internal class Robot : IHostedService
    {
        private readonly RobotUser robotUser;
        private readonly ILogger logger;
        private readonly AccountManager accountManager;
        private readonly ProjectManager projectManager;
        public Robot(RobotUser robotUser, ILogger<Robot> logger, AccountManager accountManager, ProjectManager projectManager)
        {
            this.accountManager = accountManager;
            this.projectManager = projectManager;
            this.logger = logger;
            this.robotUser = robotUser;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await robotUser.StartAsync();
            await accountManager.LoadAccountAsync();
          await  projectManager.LoadProjectAsync();
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}