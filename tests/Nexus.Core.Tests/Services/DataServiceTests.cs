using Microsoft.Extensions.Logging;
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
                [backendSource1] = new[] { new ResourceCatalog(id: "/A/B/C"), new ResourceCatalog(id: "/D/E/F")},
                [backendSource2] = new[] { new ResourceCatalog(id: "/G/H/I"), new ResourceCatalog(id: "/J/K/L")},
                [backendSource3] = new[] { new ResourceCatalog(id: "/M/N/O"), new ResourceCatalog(id: "/A/B/C")}
            };

            var data1 = new Dictionary<DateTime, double> { [begin.AddDays(0)] = 0.5, [begin.AddDays(1)] = 0.9 };
            var data2 = new Dictionary<DateTime, double> { [begin.AddDays(0)] = 0.6, [begin.AddDays(1)] = 0.8 };

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

            var catalogState = new CatalogState() { BackendSourceToCatalogsMap = backendSourceToCatalogsMap };

            var appState = new AppState()
            {
                CatalogState = catalogState
            };

            var logger = Mock.Of<ILogger<DataService>>();

            // data service
            var dataService = new DataService(appState, dataControllerService, default, logger);

            // act
            var availability = await dataService.GetAvailabilityAsync("/A/B/C", begin, end, AvailabilityGranularity.Day, CancellationToken.None);

            // assert
            Assert.Equal(2, availability.Length);

            Assert.Equal(backendSource1, availability[0].BackendSource);
            Assert.Equal(data1, availability[0].Data);

            Assert.Equal(backendSource3, availability[1].BackendSource);
            Assert.Equal(data2, availability[1].Data);
        }

        delegate void GobbleReturns(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);

        [Fact]
        public async Task CanExportAsync()
        {
            // create dirs
            var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(root);

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

            var databaseManager = Mock.Of<IDatabaseManager>();

            Mock.Get(databaseManager)
                .Setup(databaseManager => databaseManager.TryReadFirstAttachment(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<EnumerationOptions>(), 
                    out It.Ref<Stream>.IsAny))
                .Callback(new GobbleReturns((string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment) =>
                {
                    attachment = new MemoryStream();
                }))
                .Returns(true);

            Mock.Get(databaseManager)
                .Setup(databaseManager => databaseManager.WriteExportFile(It.IsAny<string>()))
                .Returns<string>((fileName) => File.OpenWrite(Path.Combine(root, fileName)));

            var logger = Mock.Of<ILogger<DataService>>();

            // catalog items
            var representation1 = new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: samplePeriod, detail: "E") 
            { 
                BackendSource = backendSource1
            };

            var resource1 = new Resource(id: "Resource1");
            var catalog1 = new ResourceCatalog(id: "/A/B/C");
            var catalogItem1 = new CatalogItem(catalog1, resource1, representation1);

            var representation2 = new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: samplePeriod, detail: "J") 
            { 
                BackendSource = backendSource2
            };

            var resource2 = new Resource(id: "Resource2");
            var catalog2 = new ResourceCatalog(id: "/F/G/H");
            var catalogItem2 = new CatalogItem(catalog2, resource2, representation2);

            // export parameters
            var exportParameters = new ExportParameters()
            {
                Begin = begin,
                End = end,
                FilePeriod = TimeSpan.FromSeconds(10),
                Writer = "A",
                ResourcePaths = new[] { catalogItem1.GetPath(), catalogItem2.GetPath() }
            };

            // data service
            var dataService = new DataService(default, dataControllerService, databaseManager, logger);

            // act
            try
            {
                var zipFileName = await dataService
                    .ExportAsync(exportParameters, new[] { catalogItem1, catalogItem2 }, Guid.NewGuid(), CancellationToken.None);

                // assert
                var zipFile = Path.Combine(root, zipFileName);
                var unzipFolder = Path.GetDirectoryName(zipFile);

                ZipFile.ExtractToDirectory(zipFile, unzipFolder);

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
