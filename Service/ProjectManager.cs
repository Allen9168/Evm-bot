using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soha.Service.Project;
using SoHa_Bot.Model;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Soha.Service
{
    public sealed class ProjectManager
    {
        public readonly ILogger logger;
        public readonly ILifetimeScope lifetimeScope;
        private readonly RobotUser robotUser;
        public readonly Dictionary<string, List<IProject>> Projects = new Dictionary<string, List<IProject>>();

        public ProjectManager(ILifetimeScope lifetimeScope, ILogger<ProjectManager> logger, RobotUser robotUser)
        {
            this.logger = logger;
            this.lifetimeScope = lifetimeScope;
            this.robotUser = robotUser;
        }
        public async Task LoadProjectAsync()
        {
            string Current = Path.Combine(Directory.GetCurrentDirectory(), "Config", "Project");
            logger.LogInformation($"[LoadProject]");

            List<IProject> projects = new List<IProject>();
            foreach (string projectPath in Directory.GetFiles(Current, "*.yaml"))
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddYamlFile(projectPath)
                    .Build();
                ProjectConfig projectConfig = configuration.Get<ProjectConfig>();
                if (projectConfig.Enabled)
                {
                    IProject project = null;
                    string model = projectConfig.Model.ToLower();
                    if (model.Equals("swap"))
                    {
                        project = lifetimeScope.Resolve<SwapService>();
                        await project.StartAsync(projectConfig);
                    }
                    else if (model.Equals("invoke"))
                    {
                        project = lifetimeScope.Resolve<InvokeService>();
                        await project.StartAsync(projectConfig);
                    }
                    else if (model.Equals("burning"))
                    {
                        project = lifetimeScope.Resolve<BurningService>();
                        await project.StartAsync(projectConfig);
                    }
                    else if (model.Equals("transactiom"))
                    {
                        project = lifetimeScope.Resolve<TransactionService >();
                        await project.StartAsync(projectConfig);
                    }
                    projects.Add(project);
                }
            }
            foreach (var project in projects)
            {
                _ = Task.Run(project.UpdateAsync);
            }
        }
    }
}
