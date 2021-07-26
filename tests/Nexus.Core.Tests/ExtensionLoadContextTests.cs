using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.Core.Tests
{
    public class ExtensionLoadContextTests
    {
        [Fact]
        public async Task CanDiscoverVersionsAsync_GitHub()
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("2.7.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-2.7.0/Gearboy-2.7.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0-beta-1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0-beta-1/Gearboy-3.0.0-beta-1-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0/Gearboy-3.0.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.1/Gearboy-3.0.1-Windows.zip"),
            };

            var extensionReference = new Dictionary<string, string>()
            {
                ["Provider"] = "GitHub",
                ["User"] = "drhelius",
                ["Project"] = "Gearboy",
                ["Release"] = "gearboy-2.7.0",
                ["AssetSelector"] = @"Windows\.zip$"
            };

            var extensionLoadContext = new ExtensionLoadContext(extensionReference);

            var actual = (await extensionLoadContext
                .DiscoverVersionsAsync())
                .Take(4);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Fact]
        public async Task CanDiscoverVersionsAsync_GitLab()
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("2.7.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-2.7.0/Gearboy-2.7.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0-beta-1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0-beta-1/Gearboy-3.0.0-beta-1-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.0", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.0/Gearboy-3.0.0-Windows.zip"),
                new DiscoveredExtensionVersion("3.0.1", "https://github.com/drhelius/Gearboy/releases/download/gearboy-3.0.1/Gearboy-3.0.1-Windows.zip"),
            };

            var extensionReference = new Dictionary<string, string>()
            {
                ["Provider"] = "GitLab",
                ["User"] = "recalbox",
                ["Project"] = "recalbox",
                ["Release"] = "7.2-Reloaded",
                ["AssetSelector"] = @"Windows\.zip$"
            };

            var extensionLoadContext = new ExtensionLoadContext(extensionReference);

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
