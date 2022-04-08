using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using Xunit;

namespace DataSource
{
    public class DataSourceControllerTests : IClassFixture<DataSourceControllerFixture>
    {
        private DataSourceControllerFixture _fixture;

        public DataSourceControllerTests(DataSourceControllerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        internal async Task CanMergeConfiguration()
        {
            // Arrange
            DataSourceContext dataSourceContext = null!;

            var dataSource = Mock.Of<IDataSource>();

            Mock.Get(dataSource)
              .Setup(dataSource => dataSource.SetContextAsync(It.IsAny<DataSourceContext>(), It.IsAny<CancellationToken>()))
              .Callback<DataSourceContext, CancellationToken>((context, cancellationToken) => dataSourceContext = context);

            var registrationConfiguration = new Dictionary<string, string>()
            {
                ["foo"] = "to_be_overriden",
                ["foo3"] = "bat",
            };

            var registration = new DataSourceRegistration(
                Type: default!,
                ResourceLocator: default!,
                Configuration: registrationConfiguration,
                Publish: true);

            var userConfiguration = new Dictionary<string, string>()
            {
                ["foo"] = "bar",
                ["foo2"] = "baz",
            };

            var controller = new DataSourceController(
                dataSource,
                registration,
                userConfiguration,
                default!,
                default!,
                default!,
                NullLogger<DataSourceController>.Instance);

            var expectedConfiguration = new Dictionary<string, string>()
            {
                ["foo"] = "bar",
                ["foo2"] = "baz",
                ["foo3"] = "bat",
            };

            // Act
            await controller.InitializeAsync(default!, default!, default);
            var actualConfiguration = dataSourceContext.Configuration;

            // Assert
            Assert.True(
                new SortedDictionary<string, string>(expectedConfiguration)
                .SequenceEqual(new SortedDictionary<string, string>(actualConfiguration)));
        }

        [Fact]
        internal async Task CanGetAvailability()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default!, default!, CancellationToken.None);

            var catalogId = Sample.AccessibleCatalogId;
            var begin = new DateTime(2020, 01, 01, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var actual = await controller.GetAvailabilityAsync(catalogId, begin, end, CancellationToken.None);

            var expectedData = new Dictionary<DateTime, double>()
            {
                [new DateTime(2020, 01, 01)] = 0.90000134808942744,
                [new DateTime(2020, 01, 02)] = 0.96041512538698282,
            };

            var actualConverted = actual.Data.ToDictionary(entry => entry.Key, entry => entry.Value);
            Assert.True(expectedData.SequenceEqual(new SortedDictionary<DateTime, double>(actualConverted)));
        }

        [Fact]
        public async Task CanGetTimeRange()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default!, default!, CancellationToken.None);

            var catalogId = Sample.AccessibleCatalogId;
            var actual = await controller.GetTimeRangeAsync(catalogId, CancellationToken.None);

            Assert.Equal(DateTime.MinValue, actual.Begin);
            Assert.Equal(DateTime.MaxValue, actual.End);
        }

        [Fact]
        public async Task CanCheckIsDataOfDayAvailable()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default!, default!, CancellationToken.None);

            var day = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var catalogId = Sample.AccessibleCatalogId;
            var actual = await controller.IsDataOfDayAvailableAsync(catalogId, day, CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task CanRead()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), default!, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromSeconds(1);

            // resource 1
            var resourcePath1 = $"{Sample.AccessibleCatalogId}/V1/1_s";
            var catalogItem1 = (await controller.GetCatalogAsync(Sample.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath1);
            var catalogItemRequest1 = new CatalogItemRequest(catalogItem1, default, default!);

            var pipe1 = new Pipe();
            var dataWriter1 = pipe1.Writer;

            // resource 2
            var resourcePath2 = $"{Sample.AccessibleCatalogId}/T1/1_s";
            var catalogItem2 = (await controller.GetCatalogAsync(Sample.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath2);
            var catalogItemRequest2 = new CatalogItemRequest(catalogItem2, default, default!);

            var pipe2 = new Pipe();
            var dataWriter2 = pipe2.Writer;

            // combine
            var catalogItemRequestPipeWriters = new CatalogItemRequestPipeWriter[]
            {
                new CatalogItemRequestPipeWriter(catalogItemRequest1, dataWriter1),
                new CatalogItemRequestPipeWriter(catalogItemRequest2, dataWriter2)
            };

            var readingGroups = new DataReadingGroup[]
            {
                new DataReadingGroup(controller, catalogItemRequestPipeWriters)
            };

            double[] result1 = new double[86401];
            double[] result2 = new double[86401];

            var writing = Task.Run(async () =>
            {
                Memory<byte> resultBuffer1 = result1.AsMemory().Cast<double, byte>();
                var stream1 = pipe1.Reader.AsStream();

                Memory<byte> resultBuffer2 = result2.AsMemory().Cast<double, byte>();
                var stream2 = pipe2.Reader.AsStream();

                while (resultBuffer1.Length > 0 || resultBuffer2.Length > 0)
                {
                    // V1
                    var readBytes1 = await stream1.ReadAsync(resultBuffer1);

                    if (readBytes1 == 0)
                        throw new Exception("The stream stopped early.");

                    resultBuffer1 = resultBuffer1.Slice(readBytes1);

                    // T1
                    var readBytes2 = await stream2.ReadAsync(resultBuffer2);

                    if (readBytes2 == 0)
                        throw new Exception("The stream stopped early.");

                    resultBuffer2 = resultBuffer2.Slice(readBytes2);
                }
            });

            var reading = DataSourceController.ReadAsync(
                begin,
                end,
                samplePeriod,
                readingGroups,
                new DataOptions() { ReadChunkSize = 20000 },
                progress: default,
                NullLogger<DataSourceController>.Instance,
                CancellationToken.None);

            await Task.WhenAll(writing, reading);

            // /SAMPLE/ACCESSIBLE/V1/1_s
            Assert.Equal(6.5, result1[0], precision: 1);
            Assert.Equal(6.7, result1[10 * 60 + 1], precision: 1);
            Assert.Equal(7.9, result1[01 * 60 * 60 + 2], precision: 1);
            Assert.Equal(8.1, result1[02 * 60 * 60 + 3], precision: 1);
            Assert.Equal(7.5, result1[10 * 60 * 60 + 4], precision: 1);

            // /SAMPLE/ACCESSIBLE/T1/1_s
            Assert.Equal(6.5, result2[0], precision: 1);
            Assert.Equal(6.7, result2[10 * 60 + 1], precision: 1);
            Assert.Equal(7.9, result2[01 * 60 * 60 + 2], precision: 1);
            Assert.Equal(8.1, result2[02 * 60 * 60 + 3], precision: 1);
            Assert.Equal(7.5, result2[10 * 60 * 60 + 4], precision: 1);
        }

        [Fact]
        public async Task CanReadAsStream()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), default!, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
            var resourcePath = "/SAMPLE/ACCESSIBLE/T1/1_s";
            var catalogItem = (await controller.GetCatalogAsync(Sample.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath);
            var catalogItemRequest = new CatalogItemRequest(catalogItem, default, default!);

            var stream = controller.ReadAsStream(
                begin,
                end,
                catalogItemRequest,
                new DataOptions() { ReadChunkSize = 10000 },
                NullLogger<DataSourceController>.Instance);

            double[] result = new double[86401];

            await Task.Run(async () =>
            {
                Memory<byte> resultBuffer = result.AsMemory().Cast<double, byte>();

                while (resultBuffer.Length > 0)
                {
                    var readBytes = await stream.ReadAsync(resultBuffer);

                    if (readBytes == 0)
                        throw new Exception("The stream stopped early.");

                    resultBuffer = resultBuffer.Slice(readBytes);
                }
            });

            Assert.Equal(86401 * sizeof(double), stream.Length);
            Assert.Equal(6.5, result[0], precision: 1);
            Assert.Equal(6.7, result[10 * 60 + 1], precision: 1);
            Assert.Equal(7.9, result[01 * 60 * 60 + 2], precision: 1);
            Assert.Equal(8.1, result[02 * 60 * 60 + 3], precision: 1);
            Assert.Equal(7.5, result[10 * 60 * 60 + 4], precision: 1);
        }

        [Fact]
        public async Task CanReadCached()
        {
            // Arrange
            var expected1 = new double[] { 65, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 101 };
            var expected2 = new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 };

            var begin = new DateTime(2020, 01, 01, 23, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 1, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromHours(1);

            var representationBase1 = new Representation(NexusDataType.INT32, TimeSpan.FromMinutes(30), RepresentationKind.Original);
            var representation1 = new Representation(NexusDataType.INT32, TimeSpan.FromHours(1), RepresentationKind.Mean);
            var representation2 = new Representation(NexusDataType.INT32, TimeSpan.FromHours(1), RepresentationKind.Original);

            var resource1 = new ResourceBuilder("id1")
                .AddRepresentation(representationBase1)
                .Build();

            var resource2 = new ResourceBuilder("id2")
                .AddRepresentation(representation2)
                .Build();

            var catalog = new ResourceCatalogBuilder("/C1")
                .AddResource(resource1)
                .AddResource(resource2)
                .Build();

            var baseItem1 = new CatalogItem(catalog, resource1, representationBase1);
            var catalogItem1 = new CatalogItem(catalog, resource1, representation1);
            var catalogItem2 = new CatalogItem(catalog, resource2, representation2);

            var request1 = new CatalogItemRequest(catalogItem1, baseItem1, default!);
            var request2 = new CatalogItemRequest(catalogItem2, default, default!);

            var pipe1 = new Pipe();
            var pipe2 = new Pipe();

            var catalogItemRequestPipeWriters = new[]
            {
                new CatalogItemRequestPipeWriter(request1, pipe1.Writer),
                new CatalogItemRequestPipeWriter(request2, pipe2.Writer)
            };

            /* IDataSource */
            var dataSource = Mock.Of<IDataSource>();

            Mock.Get(dataSource)
               .Setup(dataSource => dataSource.ReadAsync(
                   It.IsAny<DateTime>(),
                   It.IsAny<DateTime>(),
                   It.IsAny<ReadRequest[]>(),
                   It.IsAny<IProgress<double>>(),
                   It.IsAny<CancellationToken>())
               )
               .Callback<DateTime, DateTime, ReadRequest[], IProgress<double>, CancellationToken>((currentBegin, currentEnd, requests, progress, cancellationToken) =>
               {
                   var request = requests[0];
                   var intData = MemoryMarshal.Cast<byte, int>(request.Data.Span);

                   if (request.CatalogItem.Resource.Id == catalogItem1.Resource.Id &&
                       currentBegin == begin)
                   {
                       Assert.Equal(2, intData.Length);
                       intData[0] = 33; request.Status.Span[0] = 1;
                       intData[1] = 97; request.Status.Span[1] = 1;

                   }
                   else if (request.CatalogItem.Resource.Id == catalogItem1.Resource.Id && 
                            currentBegin == new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc))
                   {
                       Assert.Equal(2, intData.Length);
                       intData[0] = 100; request.Status.Span[0] = 1;
                       intData[1] = 102; request.Status.Span[1] = 1;
                   }
                   else if (request.CatalogItem.Resource.Id == "id2")
                   {
                       Assert.Equal(26, intData.Length);

                       for (int i = 0; i < intData.Length; i++)
                       {
                           intData[i] = i;
                           request.Status.Span[i] = 1;
                       }
                   }
                   else
                   {
                       throw new Exception("This should never happen.");
                   }
               })
               .Returns(Task.CompletedTask);

            /* IProcessingService */
            var processingService = Mock.Of<IProcessingService>();

            Mock.Get(processingService)
                .Setup(processingService => processingService.Process(
                   It.IsAny<NexusDataType>(),
                   It.IsAny<RepresentationKind>(),
                   It.IsAny<ReadOnlyMemory<byte>>(),
                   It.IsAny<ReadOnlyMemory<byte>>(),
                   It.IsAny<Memory<double>>(),
                   It.IsAny<int>()))
                .Callback<NexusDataType, RepresentationKind, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, Memory<double>, int>(
                (dataType, kind, data, status, targetBuffer, blockSize) =>
                {
                   Assert.Equal(NexusDataType.INT32, dataType);
                   Assert.Equal(RepresentationKind.Mean, kind);
                   Assert.Equal(8, data.Length);
                   Assert.Equal(2, status.Length);
                   Assert.Equal(1, targetBuffer.Length);
                   Assert.Equal(2, blockSize);

                   targetBuffer.Span[0] = (MemoryMarshal.Cast<byte, int>(data.Span)[0] + MemoryMarshal.Cast<byte, int>(data.Span)[1]) / 2.0;
                });

            /* IProcessingService */
            var cacheService = new Mock<ICacheService>();

            var uncachedIntervals = new List<Interval>
            {
                new Interval(begin, new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc)),
                new Interval(new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc), end)
            };

            cacheService
                .Setup(cacheService => cacheService.ReadAsync(
                   It.IsAny<CatalogItem>(),
                   It.IsAny<DateTime>(),
                   It.IsAny<Memory<double>>(),
                   It.IsAny<CancellationToken>())
                )
                .Callback<CatalogItem, DateTime, Memory<double>, CancellationToken>((item, begin, targetBuffer, cancellationToken) =>
                {
                    var offset = 1;
                    var length = 24;
                    targetBuffer.Span.Slice(offset, length).Fill(-1);
                })
                .Returns(Task.FromResult(uncachedIntervals));

            /* DataSourceController */
            var registration = new DataSourceRegistration("a", new Uri("http://xyz"), new Dictionary<string, string>(), default);

            var dataSourceController = new DataSourceController(
                dataSource,
                registration,
                new Dictionary<string, string>(),
                processingService,
                cacheService.Object,
                new DataOptions(),
                NullLogger<DataSourceController>.Instance);

            var catalogCache = new ConcurrentDictionary<string, ResourceCatalog>() { [catalog.Id] = catalog };

            await dataSourceController.InitializeAsync(catalogCache, NullLogger.Instance, CancellationToken.None);

            // Act
            await dataSourceController.ReadAsync(
                begin, 
                end, 
                samplePeriod,
                catalogItemRequestPipeWriters,
                new Progress<double>(),
                CancellationToken.None);

            // Assert
            var actual1 = MemoryMarshal.Cast<byte, double>((await pipe1.Reader.ReadAsync()).Buffer.First.Span).ToArray();
            var actual2 = MemoryMarshal.Cast<byte, double>((await pipe2.Reader.ReadAsync()).Buffer.First.Span).ToArray();

            Assert.True(expected1.SequenceEqual(actual1));
            Assert.True(expected2.SequenceEqual(actual2));

            cacheService
                .Verify(cacheService => cacheService.UpdateAsync(
                   catalogItem1,
                   new DateTime(2020, 01, 01, 23, 0, 0, DateTimeKind.Utc),
                   It.IsAny<Memory<double>>(),
                   uncachedIntervals,
                   It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
