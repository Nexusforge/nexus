using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Serilog;
using System;
using System.Globalization;

namespace Nexus
{
    internal class Program
    {
        #region Properties

        public static string Language { get; private set; }

        #endregion

        #region Methods

        public static void Main(string[] args)
        {
            // culture
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // configuration
            var configuration = NexusOptionsBase.BuildConfiguration(args);

            // logging (https://nblumhardt.com/2019/10/serilog-in-aspnetcore-3/)
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            // run
            try
            {
                Program.Language = configuration.GetValue<string>("General:Language");

                Program
                    .CreateHostBuilder(Environment.CurrentDirectory, configuration)
                    .Build()
                    .Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application start-up failed.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateHostBuilder(string currentDirectory, IConfiguration configuration) => 
            Host.CreateDefaultBuilder()

                .UseSerilog()

                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddConfiguration(configuration);
                })

                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    var serverOptions = configuration
                        .GetSection(ServerOptions.Section)
                        .Get<ServerOptions>();

                    var baseUrl = $"{serverOptions.HttpScheme}://{serverOptions.HttpAddress}:{serverOptions.HttpPort}";

                    webBuilder.UseUrls(baseUrl);
                    webBuilder.UseContentRoot(currentDirectory);
                });

        #endregion
    }
}
