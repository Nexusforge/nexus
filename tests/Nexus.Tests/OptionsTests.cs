using Microsoft.Extensions.Configuration;
using Nexus.Core;
using System;
using Xunit;

namespace Nexus.Tests
{
    public class OptionsTests
    {
        private static object _lock = new object();

        [InlineData(GeneralOptions.Section, typeof(GeneralOptions))]
        [InlineData(ServerOptions.Section, typeof(ServerOptions))]
        [InlineData(PathsOptions.Section, typeof(PathsOptions))]
        [InlineData(SecurityOptions.Section, typeof(SecurityOptions))]
        [InlineData(UsersOptions.Section, typeof(UsersOptions))]
        [InlineData(SmtpOptions.Section, typeof(SmtpOptions))]
        [InlineData(AggregationOptions.Section, typeof(AggregationOptions))]
        [Theory]
        public void CanBindOptions<T>(string section, Type optionsType)
        {
            var options = (NexusOptionsBase)Activator.CreateInstance(optionsType);
            var configuration = Program.BuildConfiguration(new string[0]);
            configuration.GetSection(section).Bind(options);

            Assert.Equal(section, options.BlindSample);
        }

        [Fact]
        public void CanReadAppsettingsJson()
        {
            var configuration = Program.BuildConfiguration(new string[0]);
            var options = new SmtpOptions();
            configuration.GetSection(SmtpOptions.Section).Bind(options);

            Assert.Equal(25, options.Port);
        }

        [Fact]
        public void CanOverrideAppsettingsJson_With_Ini()
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_PATHS_SETTINGS", "appsettings.ini");

                var configuration = Program.BuildConfiguration(new string[0]);
                var options = new SmtpOptions();
                configuration.GetSection(SmtpOptions.Section).Bind(options);

                Environment.SetEnvironmentVariable("NEXUS_PATHS_SETTINGS", null);

                Assert.Equal(26, options.Port);
            }
        }

        [Fact]
        public void CanOverrideIni_With_EnvironmentVariable()
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_PATHS_SETTINGS", "appsettings.ini");
                Environment.SetEnvironmentVariable("NEXUS_SMTP_PORT", "27");

                var configuration = Program.BuildConfiguration(new string[0]);
                var options = new SmtpOptions();
                configuration.GetSection(SmtpOptions.Section).Bind(options);

                Environment.SetEnvironmentVariable("NEXUS_PATHS_SETTINGS", null);
                Environment.SetEnvironmentVariable("NEXUS_SMTP_PORT", null);

                Assert.Equal(27, options.Port);
            }
        }

        [InlineData("SMTP:PORT=28")]
        [InlineData("/SMTP:PORT=28")]
        [InlineData("--SMTP:PORT=28")]

        [InlineData("smtp:port=28")]
        [InlineData("/smtp:port=28")]
        [InlineData("--smtp:port=28")]

        [Theory]
        public void CanOverrideEnvironmentVariable_With_CommandLineParameter1(string arg)
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_SMTP_PORT", "27");

                var configuration = Program.BuildConfiguration(new string[] { arg });
                var options = new SmtpOptions();
                configuration.GetSection(SmtpOptions.Section).Bind(options);

                Environment.SetEnvironmentVariable("NEXUS_SMTP_PORT", null);

                Assert.Equal(28, options.Port);
            }
        }
    }
}