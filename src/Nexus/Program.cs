using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Nexus.Core;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Environment;

namespace Nexus
{
    public class Program
    {
        #region Methods

        public static async Task<int> Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // check interactivity
            var isWindowsService = args.Contains("--non-interactive");

            // configuration
            var settingsPath = Environment.GetEnvironmentVariable("NEXUS:PATHS:SETTINGS");

            if (string.IsNullOrWhiteSpace(settingsPath))
                settingsPath = Environment.GetEnvironmentVariable("NEXUS__PATHS__SETTINGS");

            if (string.IsNullOrWhiteSpace(settingsPath))
                settingsPath = PathsOptions.DefaultSettingsPath;

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddIniFile(settingsPath, optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            // service vs. interactive
            var hostBuilder = Program.CreateHostBuilder(Environment.CurrentDirectory, configuration);

            if (isWindowsService)
                await hostBuilder
                    .UseWindowsService()
                    .Build()
                    .RunAsync();
            else
                await hostBuilder
                    .Build()
                    .RunAsync();

            return 0;
        }

        public static IHostBuilder CreateHostBuilder(string currentDirectory, IConfiguration configuration) => 
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.Sources.Clear();
                    builder.AddConfiguration(configuration);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddFilter<ConsoleLoggerProvider>("Microsoft", LogLevel.None);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development")
                    {
                        var serverOptions = new ServerOptions();
                        configuration.GetSection(ServerOptions.Section).Bind(serverOptions);

                        var baseUrl = $"{serverOptions.HttpScheme}://{serverOptions.HttpAddress}:{serverOptions.HttpPort}";
                        webBuilder.UseUrls(baseUrl);
                    }

                    webBuilder.UseContentRoot(currentDirectory);
                    webBuilder.SuppressStatusMessages(true);
                });

        #endregion
    }
}
