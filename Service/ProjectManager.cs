using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soha.Model;
using Soha.Service.Project;
using SoHa_Bot.Model;
using System;
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
            logger.LogInformation($"[加载项目]");

            // ① 检查目录是否存在
            if (!Directory.Exists(Current))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ 未找到项目配置目录：Config/Project");
                Console.ResetColor();
                Environment.Exit(1);
                return;
            }

            List<IProject> projects = new List<IProject>();

            // ② 遍历所有 YAML 文件
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

                    switch (model)
                    {
                        case "swap":
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"检测添加流动性启动：{Path.GetFileName(projectPath)}");
                            Console.ResetColor();
                            project = lifetimeScope.Resolve<SwapService>();
                            break;

                        case "invoke":
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"检测合约调用启动：{Path.GetFileName(projectPath)}");
                            Console.ResetColor();
                            project = lifetimeScope.Resolve<InvokeService>();
                            break;

                        case "burning":
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"燃烧模式启动：{Path.GetFileName(projectPath)}");
                            Console.ResetColor();
                            project = lifetimeScope.Resolve<BurningService>();
                            break;

                        case "transaction":
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"检测地址转账模式启动：{Path.GetFileName(projectPath)}");
                            Console.ResetColor();
                            project = lifetimeScope.Resolve<TransactionService>();
                            break;


                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"未知的项目类型: {model} ({Path.GetFileName(projectPath)})");
                            Console.ResetColor();
                            continue;
                    }

                    await project.StartAsync(projectConfig);
                    projects.Add(project);
                }
            }

            // ③ 检查是否有启用的项目
            if (projects.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("未检测到任何启用的项目。");
                Console.WriteLine("请前往配置文件夹检测是否有启动的项目");
                Console.ResetColor();
                Environment.Exit(1);
                return;
            }

            // ④ 启动后台任务
            foreach (var project in projects)
            {
                _ = Task.Run(project.UpdateAsync);
            }

            // ⑤ 输出成功提示
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"成功加载 {projects.Count} 个项目。");
            Console.ResetColor();
        }
    }
}
