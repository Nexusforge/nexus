using Nexus.Logging;
using Nexus.Services;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationBuildExtensions
	{
		public static IConfigurationBuilder AddLoggingConfiguration(this IConfigurationBuilder builder, LogLevelUpdater updater, params string[] parentPath)
		{
			return builder.Add(new LoggingConfigurationSource(updater, parentPath));
		}
	}
}