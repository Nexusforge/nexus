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
            // 1. Prepare environment variables (server url and filters)

            /* Seq server URL */
            Environment.SetEnvironmentVariable("NEXUS_LOGGING_SEQ_SERVERURL", "http://localhost:5341");

            /* How filtering rules are applied:
             * https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?tabs=aspnetcore2x&view=aspnetcore-5.0#how-filtering-rules-are-applied
             */

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

                /* Add seq sink */
                if (configuration.GetSection("Logging:Seq").Exists())
                    logging.AddSeq(configuration.GetSection("Logging:Seq"));

                /* Get SerilogLoggerProvider provider to be able to dispose it later. */
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
            var context1 = new { Amount = 108, Message = "Hello" };
            var context2 = new { Amount2 = 108, Message2 = "Hello" };

            logger.LogInformation("Log with template with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context1);

            // 3.4 Log with scope with template
            using (var scopeWithTemplate = logger.BeginScope("My templated scope message with parameters {ScopeText}, {ScopeNumber} and {ScopeAnonymousType}.", "A", 2.59, context1))
            {
                logger.LogInformation("Log with scope with template with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context1);
            }

            // 3.5 Log with double scope with template
            using (var scopeWithTemplate1 = logger.BeginScope("My templated scope message 1 with parameters {ScopeText1}, {ScopeNumber} and {ScopeAnonymousType}.", "A", 2.59, context1))
            {
                using (var scopeWithTemplate2 = logger.BeginScope("My templated scope message 2 with parameters {ScopeText2}, {ScopeNumber} and {ScopeAnonymousType}.", "A", 3.59, context2))
                {
                    logger.LogInformation("Log with double scope with template with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context1);
                }
            }

            // 3.6 Log with scope with state
            using (var scopeWithState = logger.BeginScope(context1))
            {
                logger.LogInformation("Log with scope with state with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context1);
            }

            // 3.7 Log with double scope with state
            using (var scopeWithState1 = logger.BeginScope(context1))
            {
                using (var scopeWithState2 = logger.BeginScope(context2))
                {
                    logger.LogInformation("Log with double scope with state with parameters {Text}, {Number} and {AnonymousType}.", "A", 2.59, context1);
                }
            }

            // 3.8 Log with increased log level

            logger.LogTrace("I am increasing the log level to critical only!");

            logLevelUpdater.SetLevel(LogLevel.Critical, category: "Other.LoggingTests");

            logger.LogTrace("Trace: This should not be logged!");
            logger.LogDebug("Debug: This should not be logged!");
            logger.LogInformation("Information: This should not be logged!");
            logger.LogWarning("Warning: This should not be logged!");
            logger.LogError("Error: This should not be logged!");
            logger.LogCritical("Critical: It worked! Now change back to previous value!");

            logLevelUpdater.ResetLevel(category: "Other.LoggingTests");

            logger.LogTrace("Trace: It worked!");
            logger.LogDebug("Debug: It worked!");
            logger.LogInformation("Information: It worked!");
            logger.LogWarning("Warning: It worked!");
            logger.LogError("Error: It worked!");
            logger.LogCritical("Critical: It worked!");

            // 4. Flush log messages
            loggerProviders.ForEach(loggerProvider => loggerProvider.Dispose());
        }
    }
}