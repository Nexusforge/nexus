using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Nexus.Core;
using Nexus.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var logLeveLUpdater = new LogLevelUpdater();
            var configuration = NexusOptionsBase.BuildConfiguration(args, new LogLevelUpdater());

            // service vs. interactive
            var hostBuilder = Program.CreateHostBuilder(Environment.CurrentDirectory, configuration, logLeveLUpdater);

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

        private static IHostBuilder CreateHostBuilder(string currentDirectory, IConfiguration configuration, LogLevelUpdater logLeveLUpdater) => 
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.Sources.Clear();
                    builder.AddConfiguration(configuration);
                })
                .ConfigureLogging(logging =>
                {
                    // This is a global (!) prefilter, so set it to the lowest value.
                    // Messages with lower prio will not even reach the actual loggers.
                    logging.SetMinimumLevel(LogLevel.Trace);

                    logging.AddConfiguration(configuration.GetSection("Logging"));

                    logging.ClearProviders();
                    logging.AddConsole();

                    if (configuration.GetSection("Serilog").Exists())
                        logging.AddSeq(configuration.GetSection("Serilog"));
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton(logLeveLUpdater);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development")
                    {
                        var serverOptions = configuration
                            .GetSection(ServerOptions.Section)
                            .Get<ServerOptions>();

                        var baseUrl = $"{serverOptions.HttpScheme}://{serverOptions.HttpAddress}:{serverOptions.HttpPort}";
                        webBuilder.UseUrls(baseUrl);
                    }

                    webBuilder.UseContentRoot(currentDirectory);
                    webBuilder.SuppressStatusMessages(true);
                });

        #endregion
    }
}
