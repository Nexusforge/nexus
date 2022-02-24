using Microsoft.Extensions.Configuration;
using Nexus.Core;
using Xunit;

namespace Other
{
    public class OptionsTests
    {
        private static object _lock = new object();

        [InlineData(GeneralOptions.Section, typeof(GeneralOptions))]
        [InlineData(ServerOptions.Section, typeof(ServerOptions))]
        [InlineData(PathsOptions.Section, typeof(PathsOptions))]
        [InlineData(SecurityOptions.Section, typeof(SecurityOptions))]
        [Theory]
        public void CanBindOptions<T>(string section, Type optionsType)
        {
            var configuration = NexusOptionsBase
                .BuildConfiguration(new string[0]);

            var options = (NexusOptionsBase)configuration
                .GetSection(section)
                .Get(optionsType);

            Assert.Equal(section, options.BlindSample);
        }

        [Fact]
        public void CanReadAppsettingsJson()
        {
            var configuration = NexusOptionsBase
                .BuildConfiguration(new string[0]);

            var options = configuration
                .GetSection(ServerOptions.Section)
                .Get<ServerOptions>();

            Assert.Equal(8443, options.HttpPort);
        }

        [Fact]
        public void CanOverrideAppsettingsJson_With_Json()
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", "myappsettings.json");

                var configuration = NexusOptionsBase
                    .BuildConfiguration(new string[0]);

                var options = configuration
                    .GetSection(ServerOptions.Section)
                    .Get<ServerOptions>();

                Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", null);

                Assert.Equal(26, options.HttpPort);
            }
        }

        [Fact]
        public void CanOverrideIni_With_EnvironmentVariable()
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", "appsettings.ini");
                Environment.SetEnvironmentVariable("NEXUS_SERVER__HTTPPORT", "27");

                var configuration = NexusOptionsBase
                   .BuildConfiguration(new string[0]);

                var options = configuration
                    .GetSection(ServerOptions.Section)
                    .Get<ServerOptions>();

                Environment.SetEnvironmentVariable("NEXUS_PATHS__SETTINGS", null);
                Environment.SetEnvironmentVariable("NEXUS_SERVER__HTTPPORT", null);

                Assert.Equal(27, options.HttpPort);
            }
        }

        [InlineData("SERVER:HTTPPORT=28")]
        [InlineData("/SERVER:HTTPPORT=28")]
        [InlineData("--SERVER:HTTPPORT=28")]

        [InlineData("server:httpport=28")]
        [InlineData("/server:httpport=28")]
        [InlineData("--server:httpport=28")]

        [Theory]
        public void CanOverrideEnvironmentVariable_With_CommandLineParameter1(string arg)
        {
            lock (_lock)
            {
                Environment.SetEnvironmentVariable("NEXUS_SERVER__HTTPPORT", "27");

                var configuration = NexusOptionsBase
                    .BuildConfiguration(new string[] { arg });

                var options = configuration
                    .GetSection(ServerOptions.Section)
                    .Get<ServerOptions>();

                Environment.SetEnvironmentVariable("NEXUS_SERVER__HTTPPORT", null);

                Assert.Equal(28, options.HttpPort);
            }
        }
    }
}