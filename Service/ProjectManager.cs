using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soha.Model.Config;              // ✅ 统一使用这个命名空间
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
        public readonly Dictionary<string, List<IProject>> Projects = new();

        public ProjectManager(ILifetimeScope lifetimeScope, ILogger<ProjectManager> logger, RobotUser robotUser)
        {
            this.logger = logger;
            this.lifetimeScope = lifetimeScope;
            this.robotUser = robotUser;
        }

        public async Task LoadProjectAsync()
        {
            string root = Directory.GetCurrentDirectory();
            string projectDir = Path.Combine(root, "Config", "Project");

            logger.LogInformation("[加载项目]");

            if (!Directory.Exists(projectDir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ 未找到项目配置目录：Config/Project");
                Console.ResetColor();
                Environment.Exit(1);
                return;
            }

            var projects = new List<IProject>();
            int scannedFiles = 0;

            // 同时支持 .yaml / .yml
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(projectDir, "*.yaml", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.GetFiles(projectDir, "*.yml", SearchOption.TopDirectoryOnly));

            foreach (string fullPath in files)
            {
                scannedFiles++;
                var dir = Path.GetDirectoryName(fullPath)!;
                var name = Path.GetFileName(fullPath);

                // ⛳ 关键修正：以“文件所在目录”为 base path，并传入“文件名”，不要传绝对路径
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(dir)
                    .AddYamlFile(name, optional: false, reloadOnChange: false)
                    .Build();

                ProjectConfig cfg = configuration.Get<ProjectConfig>() ?? new ProjectConfig();

                logger.LogInformation("  · 发现配置: {File} 解析结果 => Name={Name}, Model={Model}, Enabled={Enabled}",
                    name, cfg?.Name ?? "(null)", cfg?.Model ?? "(null)", cfg?.Enabled ?? false);

                if (cfg.Enabled != true)
                    continue;

                if (string.IsNullOrWhiteSpace(cfg.Model))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠️ 缺少 Model 字段，跳过：{name}");
                    Console.ResetColor();
                    continue;
                }

                IProject project = cfg.Model.Trim().ToLowerInvariant() switch
                {
                    "swap" => lifetimeScope.Resolve<SwapService>(),
                    "invoke" => lifetimeScope.Resolve<InvokeService>(),
                    "burning" => lifetimeScope.Resolve<BurningService>(),
                    "transaction" => lifetimeScope.Resolve<TransactionService>(),
                    _ => null
                };

                if (project == null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠️ 未知的项目类型: {cfg.Model} ({name})");
                    Console.ResetColor();
                    continue;
                }

                try
                {
                    await project.StartAsync(cfg);
                    projects.Add(project);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  ✅ 启动项目: {cfg.Name} (Model: {cfg.Model})");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "  ❌ 启动失败: {Name} ({File})", cfg.Name, name);
                }
            }

            if (scannedFiles == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Config/Project 下未发现任何 .yaml / .yml 文件");
                Console.ResetColor();
                Environment.Exit(1);
                return;
            }

            if (projects.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("未检测到任何启用的项目。");
                Console.WriteLine("请前往配置文件夹检测是否有启动的项目");
                Console.ResetColor();
                Environment.Exit(1);
                return;
            }

            // 后台运行每个项目
            foreach (var p in projects)
                _ = Task.Run(p.UpdateAsync);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"成功加载 {projects.Count} 个项目。");
            Console.ResetColor();
        }
    }
}
