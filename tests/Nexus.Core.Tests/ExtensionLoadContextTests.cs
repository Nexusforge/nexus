using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.Core.Tests
{
    public class ExtensionLoadContextTests
    {
        [Theory]
        [InlineData("https://github.com/drhelius/Gearboy/releases/download/gearboy-2.7.0/Gearboy-2.7.0-Windows.zip")]
        [InlineData("HTTPS://GITHUB.COM/drhelius/Gearboy/releases/download/gearboy-2.7.0/Gearboy-2.7.0-Windows.zip")]
        public async Task CanDiscoverVersions(string resourceLocatorString)
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("2.7.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-2.7.0/Gearboy-2.7.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0-beta-1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0-beta-1/Gearboy-3.0.0-beta-1-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0/Gearboy-3.0.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.1/Gearboy-3.0.1-Windows.zip"),
            };

            var resourceLocator = new Uri(resourceLocatorString);
            var extensionLoadContext = new ExtensionLoadContext(resourceLocator);

            var actual = (await extensionLoadContext
                .DiscoverVersionsAsync())
                .Take(4);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }
    }
}
