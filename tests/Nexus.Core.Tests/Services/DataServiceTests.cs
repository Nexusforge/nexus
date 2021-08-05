using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Services
{
    public class DataServiceTests
    {
        [Fact]
        public async Task CanGetAvailabilityAsync()
        {
            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);

            var backendSource1 = new BackendSource() { Type = "A" };
            var backendSource2 = new BackendSource() { Type = "B" };
            var backendSource3 = new BackendSource() { Type = "C" };

            var backendSourceToCatalogsMap = new Dictionary<BackendSource, ResourceCatalog[]>()
            {
                [backendSource1] = new[] { new ResourceCatalog() { Id = "/A/B/C" }, new ResourceCatalog() { Id = "/D/E/F" } },
                [backendSource2] = new[] { new ResourceCatalog() { Id = "/G/H/I" }, new ResourceCatalog() { Id = "/J/K/L" } },
                [backendSource3] = new[] { new ResourceCatalog() { Id = "/M/N/O" }, new ResourceCatalog() { Id = "/A/B/C" } }
            };

            var data1 = new Dictionary<DateTime, double> { [begin.AddDays(0)] = 0.5, [begin.AddDays(1)] = 0.9 };
            var data2 = new Dictionary<DateTime, double> { [begin.AddDays(0)] = 0.6, [begin.AddDays(1)] = 0.8 };

            // options
            var pathsOptions = new PathsOptions();
            var wrappedPathsOptions = Options.Create(pathsOptions);

            // DI services
            var dataSourceController1 = Mock.Of<IDataSourceController>();
            var dataSourceController2 = Mock.Of<IDataSourceController>();

            Mock.Get(dataSourceController1)
                .Setup(s => s.GetAvailabilityAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AvailabilityGranularity>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new AvailabilityResult() { BackendSource = backendSource1, Data = data1 }));

            Mock.Get(dataSourceController2)
                .Setup(s => s.GetAvailabilityAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<AvailabilityGranularity>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new AvailabilityResult() { BackendSource = backendSource3, Data = data2 }));

            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>()))
                .Returns<BackendSource, CancellationToken>((backendSource, cancellationToken) =>
                {
                    if (backendSource.Equals(backendSource1))
                        return Task.FromResult(dataSourceController1);

                    else if (backendSource.Equals(backendSource3))
                        return Task.FromResult(dataSourceController2);

                    else
                        throw new Exception("Invalid backend source.");
                });

            var databaseManagerState = new DatabaseManagerState() { BackendSourceToCatalogsMap = backendSourceToCatalogsMap };

            var databaseManager = Mock.Of<IDatabaseManager>();

            Mock.Get(databaseManager)
                .SetupGet(s => s.State)
                .Returns(databaseManagerState);

            var logger = Mock.Of<ILogger<DataService>>();

            // data service
            var dataService = new DataService(dataControllerService, databaseManager, logger, wrappedPathsOptions);

            // act
            var availability = await dataService.GetAvailabilityAsync("/A/B/C", begin, end, AvailabilityGranularity.Day, CancellationToken.None);

            // assert
            Assert.Equal(2, availability.Length);

            Assert.Equal(backendSource1, availability[0].BackendSource);
            Assert.Equal(data1, availability[0].Data);

            Assert.Equal(backendSource3, availability[1].BackendSource);
            Assert.Equal(data2, availability[1].Data);
        }

        [Fact]
        public async Task CanExportAsync()
        {
            // create dirs
            var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(root);

            // options
            var pathsOptions = new PathsOptions() { Data = root };
            Directory.CreateDirectory(pathsOptions.Export);
            Directory.CreateDirectory(pathsOptions.Attachements);
            var wrappedPathOptions = Options.Create(pathsOptions);

            // create licenses
            Directory.CreateDirectory(Path.Combine(Path.Combine(pathsOptions.Attachements, "A_B_C")));
            Directory.CreateDirectory(Path.Combine(Path.Combine(pathsOptions.Attachements, "F_G_H")));
            await File.Create(Path.Combine(pathsOptions.Attachements, "A_B_C", "LICENSE.md")).DisposeAsync();
            await File.Create(Path.Combine(pathsOptions.Attachements, "F_G_H", "license.MD")).DisposeAsync();

            // misc
            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromSeconds(1);
            var exportId = Guid.NewGuid();

            var backendSource1 = new BackendSource() { Type = "A" };
            var backendSource2 = new BackendSource() { Type = "B" };

            // DI services
            var dataSourceController1 = Mock.Of<IDataSourceController>();
            var dataSourceController2 = Mock.Of<IDataSourceController>();

            var dataWriterController = Mock.Of<IDataWriterController>();
            var tmpUri = default(Uri);

            Mock.Get(dataWriterController)
               .Setup(s => s.WriteAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CatalogItemPipeReader[]>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
               .Callback<DateTime, DateTime, TimeSpan, TimeSpan, CatalogItemPipeReader[], IProgress<double>, CancellationToken>(
                (begin, end, samplePeriod, filePeriod, catalogItemPipeReaders, progress, cancellationToken) =>
                {
                    foreach (var catalogItemPipeReaderGroup in catalogItemPipeReaders.GroupBy(x => x.CatalogItem.Catalog))
                    {
                        var prefix = catalogItemPipeReaderGroup.Key.Id.TrimStart('/').Replace('/', '_');
                        var filePath = Path.Combine(tmpUri.LocalPath, $"{prefix}.dat");
                        File.Create(filePath).Dispose();
                    }
                });

            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>()))
                .Returns<BackendSource, CancellationToken>((backendSource, cancellationToken) =>
                {
                    if (backendSource.Equals(backendSource1))
                        return Task.FromResult(dataSourceController1);

                    else if (backendSource.Equals(backendSource2))
                        return Task.FromResult(dataSourceController2);

                    else
                        throw new Exception("Invalid backend source.");
                });

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataWriterControllerAsync(It.IsAny<Uri>(), It.IsAny<ExportParameters>(), It.IsAny<CancellationToken>()))
                .Returns<Uri, ExportParameters, CancellationToken>((uri, exportParameters, cancellationToken) =>
                {
                    tmpUri = uri;
                    return Task.FromResult(dataWriterController);
                });

            var logger = Mock.Of<ILogger<DataService>>();

            // catalog items
            var representation1 = new Representation() 
            { 
                SamplePeriod = samplePeriod, 
                Detail = "E",
                DataType = NexusDataType.FLOAT32,
                BackendSource = backendSource1
            };

            var resource1 = new Resource() { Id = Guid.NewGuid() };
            var catalog1 = new ResourceCatalog() { Id = "/A/B/C" };
            var catalogItem1 = new CatalogItem(catalog1, resource1, representation1);

            var representation2 = new Representation() 
            { 
                SamplePeriod = samplePeriod,
                Detail = "J", 
                DataType = NexusDataType.FLOAT32,
                BackendSource = backendSource2
            };

            var resource2 = new Resource() { Id = Guid.NewGuid() };
            var catalog2 = new ResourceCatalog() { Id = "/F/G/H" };
            var catalogItem2 = new CatalogItem(catalog2, resource2, representation2);

            // export parameters
            var exportParameters = new ExportParameters()
            {
                Begin = begin,
                End = end,
                FilePeriod = TimeSpan.FromSeconds(10),
                Writer = "A",
                ExportMode = ExportMode.Web,
                ResourcePaths = new[] { catalogItem1.GetPath(), catalogItem2.GetPath() }
            };

            // data service
            var dataService = new DataService(dataControllerService, default, logger, wrappedPathOptions);

            // act
            try
            {
                var zipFilePath = await dataService
                    .ExportAsync(exportParameters, new[] { catalogItem1, catalogItem2 }, Guid.NewGuid(), CancellationToken.None);

                // assert
                var unzipFolder = Path.GetDirectoryName(zipFilePath);
                ZipFile.ExtractToDirectory(zipFilePath, unzipFolder);

                Assert.True(File.Exists(Path.Combine(unzipFolder, "A_B_C.dat")));
                Assert.True(File.Exists(Path.Combine(unzipFolder, "A_B_C_LICENSE.md")));

                Assert.True(File.Exists(Path.Combine(unzipFolder, "F_G_H.dat")));
                Assert.True(File.Exists(Path.Combine(unzipFolder, "F_G_H_LICENSE.md")));

            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                    //
                }
            }
        }
    }
}
