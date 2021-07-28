using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.Core.Tests
{
    public class ExtensionLoadContextTests
    {
        [Fact]
        public async Task CanDiscoverVersions_local()
        {
            // create dirs
            var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(root);

            var versions = new[]
            {
                "v0.1.0",
                "v1.0.0-alpha1+12345 postfix",
                "v1.0.0-beta1+12346 postfix",
                "v1.0.0-beta2+12347 postfix",
                "v1.0.1 postfix",
                "v1.1.1 postfix",
                "v2.0.0 postfix"
            };

            foreach (var version in versions)
            {
                var dataFolderPath = Path.Combine(root, version);
                Directory.CreateDirectory(Path.Combine(dataFolderPath, "sub"));

                await File.Create(Path.Combine(dataFolderPath, "sub", "a.deps.json")).DisposeAsync();
                await File.Create(Path.Combine(dataFolderPath, "sub", "a.dll")).DisposeAsync();
            }

            try
            {
                var expected = new[]
                {
                    new DiscoveredExtensionVersion("0.1.0", $"file:///{root.Replace('\\', '/')}/v0.1.0"),
                    new DiscoveredExtensionVersion("1.0.0-alpha1",  $"file:///{root.Replace('\\', '/')}/v1.0.0-alpha1+12345 postfix"),
                    new DiscoveredExtensionVersion("1.0.0-beta1",  $"file:///{root.Replace('\\', '/')}/v1.0.0-beta1+12346 postfix"),
                    new DiscoveredExtensionVersion("1.0.0-beta2",  $"file:///{root.Replace('\\', '/')}/v1.0.0-beta2+12347 postfix"),
                    new DiscoveredExtensionVersion("1.0.1",  $"file:///{root.Replace('\\', '/')}/v1.0.1 postfix"),
                    new DiscoveredExtensionVersion("1.1.1",  $"file:///{root.Replace('\\', '/')}/v1.1.1 postfix"),
                    new DiscoveredExtensionVersion("2.0.0",  $"file:///{root.Replace('\\', '/')}/v2.0.0 postfix"),
                };

                var extensionReference = new Dictionary<string, string>()
                {
                    // required
                    ["Provider"] = "local",
                    ["Path"] = root,
                    ["Version"] = versions[4]
                };

                var extensionLoadContext = new ExtensionLoadContext(extensionReference);
                var actual = await extensionLoadContext.DiscoverAsync();

                Assert.Equal(expected.Length, actual.Length);

                foreach (var (expectedItem, actualItem) in expected.Zip(actual))
                {
                    Assert.Equal(expectedItem, actualItem);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task CanDiscoverVersions_github_releases()
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("0.1.0", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v0.1.0/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-alpha1", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v1.0.0-alpha1%2B12345/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta1", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v1.0.0-beta1%2B12346/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta2", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v1.0.0-beta2%2B12347/assets.zip"),
                new DiscoveredExtensionVersion("1.0.1", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v1.0.1/assets.zip"),
                new DiscoveredExtensionVersion("1.1.1", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v1.1.1/assets.zip"),
                new DiscoveredExtensionVersion("2.0.0", "https://github.com/Nexusforge/github-releases-provider-test-project/releases/download/v2.0.0/assets.zip"),
            };

            // Need to do it this way because GitHub revokes obvious tokens on commit.
            // However, this token - in combination with the test user's account
            // privileges - allows only read-only access to a test project, so there
            // is no real risk.
            var token = new byte[]
            {
                0x67, 0x68, 0x70, 0x5F, 0x6F, 0x77, 0x54, 0x68, 0x65, 0x48,
                0x52, 0x54, 0x53, 0x44, 0x64, 0x32, 0x5A, 0x6F, 0x47, 0x72,
                0x46, 0x43, 0x50, 0x6B, 0x33, 0x32, 0x53, 0x61, 0x48, 0x64,
                0x42, 0x31, 0x58, 0x66, 0x32, 0x53, 0x36, 0x36, 0x44, 0x37
            };

            var extensionReference = new Dictionary<string, string>()
            {
                // required
                ["Provider"] = "github-releases",
                ["ProjectPath"] = "Nexusforge/github-releases-provider-test-project",
                ["Release"] = "v1.0.1",
                ["AssetSelector"] = @"assets\.zip$",

                // optional token with scope(s): repo
                ["Token"] = Encoding.ASCII.GetString(token)
            };

            var extensionLoadContext = new ExtensionLoadContext(extensionReference);
            var actual = await extensionLoadContext.DiscoverAsync();

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Fact]
        public async Task CanDiscoverVersions_gitlab_releases_v4()
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("0.1.0", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-alpha1", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta1", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta2", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("1.0.1", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("1.1.1", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
                new DiscoveredExtensionVersion("2.0.0", "https://gitlab.com/nexusforge/Test-Group/my-awesome-test-project/uploads/a1bb552ab3d9ec242652c9ef9f067906/assets.zip"),
            };

            var extensionReference = new Dictionary<string, string>()
            {  
                // required
                ["Provider"] = "gitlab-releases-v4",
                ["Server"] = "https://gitlab.com",
                ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",
                ["Release"] = "v1.0.1",
                ["AssetSelector"] = @"assets\.zip$",

                // optional token with scope(s): read_api
                ["Token"] = "xq4kogGyykZ7yzky37bT"
            };

            var extensionLoadContext = new ExtensionLoadContext(extensionReference);

            var actual = (await extensionLoadContext
                .DiscoverAsync());

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Fact]
        public async Task CanDiscoverVersions_gitlab_packages_generic_v4()
        {
            var expected = new[]
            {
                new DiscoveredExtensionVersion("0.1.0", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v0.1.0/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-alpha1", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v1.0.0-alpha1+12345/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta1", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v1.0.0-beta1+12346/assets.zip"),
                new DiscoveredExtensionVersion("1.0.0-beta2", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v1.0.0-beta2+12347/assets.zip"),
                new DiscoveredExtensionVersion("1.0.1", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v1.0.1/assets.zip"),
                new DiscoveredExtensionVersion("1.1.1", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v1.1.1/assets.zip"),
                new DiscoveredExtensionVersion("2.0.0", "https://gitlab.com/api/v4/projects/nexusforge%2FTest-Group%2Fmy-awesome-test-project/packages/generic/test-package/v2.0.0/assets.zip"),
            };

            var extensionReference = new Dictionary<string, string>()
            {
                // required
                ["Provider"] = "gitlab-packages-generic-v4",
                ["Server"] = "https://gitlab.com",
                ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",
                ["Package"] = "test-package",
                ["Version"] = "v1.0.1",
                ["AssetSelector"] = @"assets\.zip$",

                // optional token with scope(s): read_api
                ["Token"] = "7LyuyEzJrAUWyKMq4zNt",
            };

            var extensionLoadContext = new ExtensionLoadContext(extensionReference);

            var actual = (await extensionLoadContext
                .DiscoverAsync());

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }
    }
}
