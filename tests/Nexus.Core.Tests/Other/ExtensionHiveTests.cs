using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using Nexus.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Other
{
    public class ExtensionHiveTests
    {
        [Fact]
        public async Task CanInstantiateExtensionsAsync()
        {
            // prepare extension
            var extensionFolderPath = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
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
                // load packages
                var pathsOptions = new PathsOptions()
                {
                    Packages = restoreRoot
                };

                var hive = new ExtensionHive(Options.Create(pathsOptions), NullLogger.Instance);

                var version = "v1.0.0-unit.test";

                var packageReference = new PackageReference()
                {
                    // required
                    ["Provider"] = "local",
                    ["Path"] = extensionFolderPath,
                    ["Version"] = version
                };

                var packageReferences = new[]
                {
                    packageReference
                };

                await hive.LoadPackagesAsync(packageReferences, CancellationToken.None);

                // instantiate
                Assert.True(hive.TryGetInstance<IDataSource>("my-unique-data-source", out var dataSource));
                Assert.NotNull(dataSource);

                Assert.True(hive.TryGetInstance<IDataWriter>("my-unique-data-writer", out var dataWriter));
                Assert.NotNull(dataWriter);

                Assert.False(hive.TryGetInstance<IDataSource>("my-unique-data-writer", out var _));
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
    }
}
