using Nexus.Core;

namespace Microsoft.Extensions.Configuration
{
    public static class EnvironmentVariablesConfigurationExtensions
    {
        public static IConfigurationBuilder AddImprovedEnvironmentVariables(this IConfigurationBuilder configurationBuilder, string prefix)
        {
            configurationBuilder.Add(new EnvironmentVariablesConfigurationProvider(prefix));
            return configurationBuilder;
        }
    }
}