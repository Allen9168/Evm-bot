using Amazon.Runtime;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AwsSignatureVersion4.Private;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Soha.Core;
using Soha.Service;
using Soha.Service.Project;
using StreamJsonRpc;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using Soha.Model.Config;

namespace Soha
{
    public sealed class Program
    {
        private static IConfigurationRoot ConfigurationBuilder { get; set; }
        private static void Main(string[] args)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "Config"))
                .AddYamlFile("logSetting.yaml")
                .Build();

            ConfigurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "Config"))
                .AddYamlFile("config.yaml")
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.Debug()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}", theme: AnsiConsoleTheme.Code)
                .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "logs", "logs.txt"))
                .CreateLogger();

            CreateHostBuilder(args).Build().Run();
        }
        public static IHostBuilder CreateHostBuilder(string[] args) =>
             Host.CreateDefaultBuilder(args)
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddHostedService<Robot>();
                 })
                 .ConfigureAppConfiguration((hostContext, configApp) =>
                 {
                     string path = Path.Combine(Directory.GetCurrentDirectory(), "Config", "config.yaml");
                     configApp.AddYamlFile(path, optional: true);
                     configApp.AddCommandLine(args);
                 })
                .ConfigureLogging((context, logging) => logging.ClearProviders())
                  .UseSerilog()
                  .UseServiceProviderFactory(new AutofacServiceProviderFactory(ApplicationContainer));

        private static void ApplicationContainer(ContainerBuilder containerBuilder)
        {
            containerBuilder.RegisterType<RpcClient>().SingleInstance();

            containerBuilder.RegisterType<AccountService>().InstancePerDependency();
            containerBuilder.RegisterType<InvokeService>().InstancePerDependency();
            containerBuilder.RegisterType<SwapService>().InstancePerDependency();
            containerBuilder.RegisterType<BurningService>().InstancePerDependency();
            containerBuilder.RegisterType<TransactionService >().InstancePerDependency();



            _ = containerBuilder.Register(static o =>
            {
                var logger = o.Resolve<ILogger<Program>>();
                string uriStr = ConfigurationBuilder.GetValue<string>("addres");
                ClientWebSocket clientWebSocket = new();
                Uri uri = new(uriStr);

                try
                {
                    clientWebSocket.ConnectAsync(uri, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                    logger.LogInformation("成功连接到 RPC: {uriStr}", uriStr);
                }
                catch (UriFormatException)
                {
                    logger.LogInformation("RPC 地址格式错误: {uriStr}", uriStr);
                    Environment.Exit(1);
                }
                catch (System.Net.WebSockets.WebSocketException ex)
                {
                    logger.LogInformation("无法连接到 RPC: {RpcUri}", uriStr);
                    logger.LogInformation("错误信息: {ErrorMessage}", ex.Message);
                    Environment.Exit(1);
                }
                catch (Exception ex)
                {
                    logger.LogInformation("连接 RPC 时出现未知错误:{ErrorMessage}", ex.Message);
                    Environment.Exit(1);
                }

                MessageHandlerBase messageHandler = new WebSocketMessageHandler(clientWebSocket);
                return messageHandler;
            }).SingleInstance();

            containerBuilder.RegisterType<RobotUser>().SingleInstance();
            containerBuilder.RegisterType<AccountManager>().SingleInstance();
            containerBuilder.RegisterType<ProjectManager>().SingleInstance();
            containerBuilder.RegisterType<TransactionsService>().SingleInstance();

            containerBuilder.RegisterType<TokenService>().SingleInstance();
            containerBuilder.RegisterType<ChainService>().SingleInstance();
        }
    }
}
