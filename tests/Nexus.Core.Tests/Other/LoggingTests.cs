using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Services;
using Seq.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Other
{
    public class LoggingTests
    {
        [Fact]
        public void CanChangeLogLevel()
        {
            var logLevelUpdater = new LogLevelUpdater();
            var configuration = NexusOptionsBase.BuildConfiguration(new string[0], logLevelUpdater);

            Assert.Equal("Information", configuration[$"Logging:LogLevel:Default"]);
            logLevelUpdater.SetLevel(LogLevel.Critical, "Default");
            Assert.Equal("Critical", configuration[$"Logging:LogLevel:Default"]);
            logLevelUpdater.ResetLevel("Default");
            Assert.Equal("Information", configuration[$"Logging:LogLevel:Default"]);

            logLevelUpdater.SetLevel(LogLevel.Critical, "Default", "MyProvider");
            Assert.Equal("Critical", configuration[$"Logging:MyProvider:LogLevel:Default"]);
            logLevelUpdater.ResetLevel("Default", "MyProvider");
            Assert.Null(configuration[$"Logging:MyProvider:LogLevel:Default"]);
        }

        [Fact]
        public void CanLogToSeq()
        {
            // 1. Prepare filters

            /* How filtering rules are applied:
             * https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?tabs=aspnetcore2x&view=aspnetcore-5.0#how-filtering-rules-are-applied
             */

            Environment.SetEnvironmentVariable("NEXUS_LOGGING_SEQ_SERVERURL", "http://localhost:5341");

            /* Seq Minimum Level (cannot override):
             * if not defined: LevelAlias.Minimum = "Trace" (https://github.com/datalust/seq-extensions-logging/blob/90a7471e0c48d1065e60338a1c8f646a85e845c8/src/Seq.Extensions.Logging/Microsoft/Extensions/Logging/SeqLoggerExtensions.cs#L101)
             * if invalid: Fallback = "Information" (https://github.com/datalust/seq-extensions-logging/blob/90a7471e0c48d1065e60338a1c8f646a85e845c8/src/Seq.Extensions.Logging/Microsoft/Extensions/Logging/SeqLoggerExtensions.cs#L131)
             */
            Environment.SetEnvironmentVariable("NEXUS_LOGGING_SEQ_MINIMUMLEVEL", "Trace");

            /* Lower the log level for a specific logger: */
            Environment.SetEnvironmentVariable("NEXUS_LOGGING_LOGLEVEL_Other.LoggingTests", "Trace");

            // 2. Build the configuration
            var logLevelUpdater = new LogLevelUpdater();
            var configuration = NexusOptionsBase.BuildConfiguration(new string[0], logLevelUpdater);

            List<ILoggerProvider> loggerProviders = default;

            var loggerFactory = LoggerFactory.Create(logging =>
            {
                /* ASP.NET Minimum Level (fallback if there is no other rule)
                 * "Minimum level is only used when there are no rules matched at all":
                 * https://github.com/datalust/seq-extensions-logging/issues/22#issuecomment-324172189
                 * 
                 * => LogLevel:Default:Information overrides this
                 */
                logging.SetMinimumLevel(LogLevel.Information);

                /* It is very important to provide the correct section! */
                logging.AddConfiguration(configuration.GetSection("Logging"));

                if (configuration.GetSection("Logging:Seq").Exists())
                    logging.AddSeq(configuration.GetSection("Logging:Seq"));

                loggerProviders = logging.Services
                   .Where(descriptor => descriptor.ImplementationFactory is not null)
                   .Select(descriptor => (ILoggerProvider)descriptor.ImplementationFactory(null))
                   .ToList();
            });

            // 3. Create a logger
            var logger = loggerFactory.CreateLogger<LoggingTests>();

            // 3.1 Log-levels
            logger.LogTrace("Trace");
            logger.LogDebug("Debug");
            logger.LogInformation("Information");
            logger.LogWarning("Warning");
            logger.LogError("Error");
            logger.LogCritical("Critical");

            // 3.2 Log with exception
            try
            {
                throw new Exception("Something went wrong?!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error");
            }

            // 3.3 Log with template
            var context = new { Amount = 108, Message = "Hello" };
            logger.LogWarning("My templated message with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context);

            // 4. Clean-up
            loggerProviders.ForEach(loggerProvider => loggerProvider.Dispose());
        }
    }
}