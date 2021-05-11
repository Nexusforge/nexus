using Microsoft.AspNetCore.Hosting;
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

namespace Nexus
{
    public class Program
    {
        #region Fields

        private static ILoggerFactory _loggerFactory;

        #endregion

        #region Properties

        public static string OptionsFilePath { get; private set; }

        public static NexusOptions Options { get; private set; }

        #endregion

        #region Methods

        public static async Task<int> Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // check interactivity
            var isWindowsService = args.Contains("--non-interactive");

            // paths
            var appdataFolderPath = Environment.GetEnvironmentVariable("NEXUS_APPDATA");

            if (string.IsNullOrWhiteSpace(appdataFolderPath))
                appdataFolderPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Nexus");

            Directory.CreateDirectory(appdataFolderPath);

            // configure logging
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            // load configuration
            Program.OptionsFilePath = Path.Combine(appdataFolderPath, "settings.json");

            if (File.Exists(Program.OptionsFilePath))
            {
                Program.Options = NexusOptions.Load(Program.OptionsFilePath);
            }
            else
            {
                Program.Options = new NexusOptions();
                Program.Options.Save(Program.OptionsFilePath);
            }

            // service vs. interactive
            if (isWindowsService)
                await Program
                    .CreateHostBuilder(Environment.CurrentDirectory)
                    .UseWindowsService()
                    .Build()
                    .RunAsync();
            else
                await Program
                    .CreateHostBuilder(Environment.CurrentDirectory)
                    .Build()
                    .RunAsync();

            return 0;
        }

        public static IHostBuilder CreateHostBuilder(string currentDirectory) => 
            Host.CreateDefaultBuilder()
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
                        webBuilder.UseUrls(Program.Options.AspBaseUrl);

                    webBuilder.UseContentRoot(currentDirectory);
                    webBuilder.SuppressStatusMessages(true);
                });

        #endregion
    }
}
