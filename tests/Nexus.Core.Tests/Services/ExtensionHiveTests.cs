﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Models;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Services
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

                var loggerFactory = Mock.Of<ILoggerFactory>();

                Mock.Get(loggerFactory)
                    .Setup(loggerFactory => loggerFactory.CreateLogger(It.IsAny<string>()))
                    .Returns(NullLogger.Instance);

                var hive = new ExtensionHive(Options.Create(pathsOptions), NullLogger<ExtensionHive>.Instance, loggerFactory);

                var version = "v1.0.0-unit.test";

                var packageReference = new PackageReference(
                    Provider: "local",
                    Configuration: new Dictionary<string, string>()
                    {
                        // required
                        ["Path"] = extensionFolderPath,
                        ["Version"] = version
                    }
                );

                var packageReferences = new[]
                {
                    packageReference
                };

                await hive.LoadPackagesAsync(packageReferences, CancellationToken.None);

                // instantiate
                Assert.True(hive.TryGetInstance<IDataSource>("TestExtensionProject.TestDataSource", out var dataSource));
                Assert.NotNull(dataSource);

                Assert.True(hive.TryGetInstance<IDataWriter>("TestExtensionProject.TestDataWriter", out var dataWriter));
                Assert.NotNull(dataWriter);

                Assert.False(hive.TryGetInstance<IDataSource>("TestExtensionProject.TestDataWriter", out var _));
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
