using Microsoft.Extensions.Logging.Abstractions;
using Nexus;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Other
{
    public class PackageControllerTests
    {
        #region Load

        [Fact]
        public async Task CanLoadAndUnloadAsync()
        {
            // prepare extension
            var extensionFolderPath = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var pathHash = new Guid(extensionFolderPath.Hash()).ToString();
            var configuration = "Debug";
            var csprojPath = "./../../../../../tests/TestExtensionProject/TestExtensionProject.csproj";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish --output {Path.Combine(extensionFolderPath, "v1.0.0-unit.test")} --configuration {configuration} {csprojPath}"
                }
            };

            process.Start();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);

            // prepare restore root
            var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");

            try
            {
                var version = "v1.0.0-unit.test";

                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "local",
                    ["Path"] = extensionFolderPath,
                    ["Version"] = version
                };

                var fileToDelete = Path.Combine(restoreRoot, "local", pathHash, version, "TestExtensionProject.dll");
                var weakReference = await this.Load_Run_and_Unload_Async(restoreRoot, fileToDelete, packageReference);

                for (int i = 0; weakReference.IsAlive && i < 10; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                // try to delete file
                File.Delete(fileToDelete);
            }
            finally
            {
                try
                {
                    Directory.Delete(restoreRoot, recursive: true);
                }
                catch { }

                try
                {
                    Directory.Delete(extensionFolderPath, recursive: true);
                }
                catch { }
            }
        }

        // https://docs.microsoft.com/en-us/dotnet/standard/assembly/unloadability
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<WeakReference> Load_Run_and_Unload_Async(
            string restoreRoot, string fileToDelete, PackageReference packageReference)
        {
            // load
            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            var assembly = await packageController.LoadAsync(restoreRoot, CancellationToken.None);

            var dataSourceType = assembly
                .ExportedTypes
                .First(type => typeof(IDataSource).IsAssignableFrom(type));

            // run
            var dataSource = (IDataSource)Activator.CreateInstance(dataSourceType);
            var exception = await Assert.ThrowsAsync<NotImplementedException>(() => dataSource.GetCatalogsAsync(CancellationToken.None));

            Assert.Equal(nameof(IDataSource.GetCatalogsAsync), exception.Message);

            // delete should fail
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Assert.Throws<UnauthorizedAccessException>(() => File.Delete(fileToDelete));

            // unload
            var weakReference = packageController.Unload();

            return weakReference;
        }

        #endregion

        #region Provider: local

        [Fact]
        public async Task CanDiscover_local()
        {
            // create dirs
            var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");

            var expected = new[]
            {
                "v2.0.0 postfix",
                "v1.1.1 postfix",
                "v1.0.1 postfix",
                "v1.0.0-beta2+12347 postfix",
                "v1.0.0-beta1+12346 postfix",
                "v1.0.0-alpha1+12345 postfix",
                "v0.1.0"
            };

            foreach (var version in expected)
            {
                var dataFolderPath = Path.Combine(root, version);
                Directory.CreateDirectory(Path.Combine(dataFolderPath, "sub"));

                await File.Create(Path.Combine(dataFolderPath, "sub", "a.deps.json")).DisposeAsync();
                await File.Create(Path.Combine(dataFolderPath, "sub", "a.dll")).DisposeAsync();
            }

            try
            {
                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "local",
                    ["Path"] = root,
                };

                var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

                var actual = (await packageController
                    .DiscoverAsync(CancellationToken.None))
                    .ToArray();

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
        public async Task CanRestore_local()
        {
            // create extension folder
            var version = "v1.0.1 postfix";
            var extensionRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var extensionRootHash = new Guid(extensionRoot.Hash()).ToString();
            var extensionFolderPath = Path.Combine(extensionRoot, version);
            Directory.CreateDirectory(Path.Combine(extensionFolderPath, "sub", "sub"));

            await File.Create(Path.Combine(extensionFolderPath, "sub", "a.deps.json")).DisposeAsync();
            await File.Create(Path.Combine(extensionFolderPath, "sub", "a.dll")).DisposeAsync();
            await File.Create(Path.Combine(extensionFolderPath, "sub", "b.dll")).DisposeAsync();
            await File.Create(Path.Combine(extensionFolderPath, "sub", "sub", "c.data")).DisposeAsync();

            // create restore folder
            var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var restoreFolderPath = Path.Combine(restoreRoot, "local", extensionRootHash, version);
            Directory.CreateDirectory(restoreRoot);

            try
            {
                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "local",
                    ["Path"] = extensionRoot,
                    ["Version"] = version
                };

                var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
                await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
            }
            finally
            {
                Directory.Delete(extensionRoot, recursive: true);
                Directory.Delete(restoreRoot, recursive: true);
            }
        }

        #endregion

        #region Provider: github_releases

        [Fact]
        public async Task CanDiscover_github_releases()
        {
            var expected = new[]
            {
                "v2.0.0",
                "v1.1.1",
                "v1.0.1",
                "v1.0.0-beta2+12347",
                "v1.0.0-beta1+12346",
                "v1.0.0-alpha1+12345",
                "v0.1.0"
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

            var packageReference = new PackageReference()
            {
                // required
                ["Provider"] = "github-releases",
                ["ProjectPath"] = "nexusforge/github-releases-provider-test-project",

                // optional token with scope(s): repo
                ["Token"] = Encoding.ASCII.GetString(token)
            };

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

            var actual = (await packageController
                .DiscoverAsync(CancellationToken.None))
                .ToArray();

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Theory]
        [InlineData(@"assets.*\.tar.gz")]
        [InlineData(@"assets.*\.zip")]
        public async Task CanRestore_github_releases(string assetSelector)
        {
            var version = "v1.0.1";

            // create restore folder
            var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var restoreFolderPath = Path.Combine(restoreRoot, "github-releases", $"nexusforge_github-releases-provider-test-project", version);
            Directory.CreateDirectory(restoreRoot);

            try
            {
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

                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "github-releases",
                    ["ProjectPath"] = "nexusforge/github-releases-provider-test-project",
                    ["Tag"] = "v1.0.1",
                    ["AssetSelector"] = assetSelector,

                    // optional token with scope(s): repo
                    ["Token"] = Encoding.ASCII.GetString(token)
                };

                var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
                await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
            }
            finally
            {
                Directory.Delete(restoreRoot, recursive: true);
            }
        }

        #endregion

        #region Provider: gitlab-releases-v4

        [Fact(Skip = "The current approach does not work. See rationale in #region gitlab-releases-v4.")]
        public async Task CanDiscover_gitlab_releases_v4()
        {
            var expected = new[]
            {
                "v2.0.0",
                "v1.1.1",
                "v1.0.1",
                "v1.0.0-beta2+12347",
                "v1.0.0-beta1+12346",
                "v1.0.0-alpha1+12345",
                "v0.1.0"
            };

            var packageReference = new PackageReference()
            {
                // required
                ["Provider"] = "gitlab-releases-v4",
                ["Server"] = "https://gitlab.com",
                ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",

                // optional token with scope(s): read_api
                ["Token"] = "doQyXYqgmFxS1LUsupue"
            };

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

            var actual = (await packageController
                .DiscoverAsync(CancellationToken.None))
                .ToArray();

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Theory(Skip = "The current approach does not work. See rationale in #region gitlab-releases-v4.")]
        [InlineData(@"assets.*\.tar.gz")]
        [InlineData(@"assets.*\.zip")]
        public async Task CanRestore_gitlab_releases_v4(string assetSelector)
        {
            var version = "v1.0.1";

            // create restore folder
            var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var restoreFolderPath = Path.Combine(restoreRoot, "gitlab-releases-v4", $"nexusforge_test-group_my-awesome-test-project", version);
            Directory.CreateDirectory(restoreRoot);

            try
            {
                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "gitlab-releases-v4",
                    ["Server"] = "https://gitlab.com",
                    ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",
                    ["Tag"] = "v1.0.1",
                    ["AssetSelector"] = assetSelector,

                    // optional token with scope(s): read_api
                    ["Token"] = "doQyXYqgmFxS1LUsupue"
                };

                var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
                await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
            }
            finally
            {
                Directory.Delete(restoreRoot, recursive: true);
            }
        }

        #endregion

        #region Provider: gitlab-packages-generic-v4

        [Fact]
        public async Task CanDiscover_gitlab_packages_generic_v4()
        {
            var expected = new[]
            {
                "v2.0.0",
                "v1.1.1",
                "v1.0.1",
                "v1.0.0-beta2+12347",
                "v1.0.0-beta1+12346",
                "v1.0.0-alpha1+12345",
                "v0.1.0"
            };

            var packageReference = new PackageReference()
            {
                // required
                ["Provider"] = "gitlab-packages-generic-v4",
                ["Server"] = "https://gitlab.com",
                ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",
                ["Package"] = "test-package",

                // optional token with scope(s): read_api
                ["Token"] = "zNSQJjP6eWpQ8k-zpvDs",
            };

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

            var actual = (await packageController
                .DiscoverAsync(CancellationToken.None))
                .ToArray();

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }

        [Theory]
        [InlineData(@"assets.*\.tar.gz")]
        [InlineData(@"assets.*\.zip")]
        public async Task CanRestore_gitlab_packages_generic_v4(string assetSelector)
        {
            var version = "v1.0.1";

            // create restore folder
            var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            var restoreFolderPath = Path.Combine(restoreRoot, "gitlab-packages-generic-v4", $"nexusforge_test-group_my-awesome-test-project", version);
            Directory.CreateDirectory(restoreRoot);

            try
            {
                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "gitlab-packages-generic-v4",
                    ["Server"] = "https://gitlab.com",
                    ["ProjectPath"] = "nexusforge/Test-Group/my-awesome-test-project",
                    ["Package"] = "test-package",
                    ["Version"] = "v1.0.1",
                    ["AssetSelector"] = assetSelector,

                    // optional token with scope(s): read_api
                    ["Token"] = "zNSQJjP6eWpQ8k-zpvDs",
                };

                var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
                await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
                Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
            }
            finally
            {
                Directory.Delete(restoreRoot, recursive: true);
            }
        }

        #endregion
    }
}
