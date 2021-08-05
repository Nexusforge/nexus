using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Serilog;
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
            var configuration = NexusOptionsBase.BuildConfiguration(args);

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

        private static IHostBuilder CreateHostBuilder(string currentDirectory, IConfiguration configuration) => 
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.Sources.Clear();
                    builder.AddConfiguration(configuration);
                })
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration.ReadFrom.Configuration(context.Configuration);
                }, writeToProviders: false)
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
