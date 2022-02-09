using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using System.Collections.Concurrent;
using System.IO.Pipelines;
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

            var backendSourceConfiguration = new Dictionary<string, string>()
            {
                ["foo"] = "to_be_overriden",
                ["foo3"] = "bat",
            };

            var backendSource = new BackendSource(
                Type: default!,
                ResourceLocator: default!,
                Configuration: backendSourceConfiguration,
                Publish: true);

            var userConfiguration = new Dictionary<string, string>()
            {
                ["foo"] = "bar",
                ["foo2"] = "baz",
            };

            var controller = new DataSourceController(
                dataSource,
                backendSource,
                userConfiguration,
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

            var catalogId = InMemory.AccessibleCatalogId;
            var begin= new DateTime(2020, 01, 01, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var actual = await controller.GetAvailabilityAsync(catalogId, begin, end, CancellationToken.None);

            var expectedData = new Dictionary<DateTime, double>()
            {
                [new DateTime(2020, 01, 01)] = 0.90000134808942744,
                [new DateTime(2020, 01, 02)] = 0.96041512538698282,
            };

            Assert.True(expectedData.SequenceEqual(new SortedDictionary<DateTime, double>(actual.Data)));
        }

        [Fact]
        public async Task CanGetTimeRange()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default!, default!, CancellationToken.None);

            var catalogId = InMemory.AccessibleCatalogId;
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
            var catalogId = InMemory.AccessibleCatalogId;
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
            var resourcePath1 = $"{InMemory.AccessibleCatalogId}/V1/1_s_mean";
            var catalogItem1 = (await controller.GetCatalogAsync(InMemory.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath1);

            var pipe1 = new Pipe();
            var dataWriter1 = pipe1.Writer;

            // resource 2
            var resourcePath2 = $"{InMemory.AccessibleCatalogId}/T1/1_s_mean";
            var catalogItem2 = (await controller.GetCatalogAsync(InMemory.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath2);

            var pipe2 = new Pipe();
            var dataWriter2 = pipe2.Writer;

            // combine
            var catalogItemPipeWriters = new CatalogItemPipeWriter[] 
            {
                new CatalogItemPipeWriter(catalogItem1, dataWriter1, default),
                new CatalogItemPipeWriter(catalogItem2, dataWriter2, default)
            };

            var readingGroups = new DataReadingGroup[] 
            { 
                new DataReadingGroup(controller, catalogItemPipeWriters) 
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

            DataSourceController.ChunkSize = 20000;

            var reading = DataSourceController.ReadAsync(
                begin,
                end, 
                samplePeriod,
                readingGroups,
                progress: default, 
                NullLogger<DataSourceController>.Instance,
                CancellationToken.None);

            await Task.WhenAll(writing, reading);

            // /IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean
            Assert.Equal(-0.059998, result1[0], precision: 6);
            Assert.Equal(8.191772, result1[10 * 60], precision: 6);
            Assert.Equal(16.290592, result1[01 * 60 * 60], precision: 6);
            Assert.Equal(15.046221, result1[02 * 60 * 60], precision: 6);
            Assert.Equal(15.274073, result1[10 * 60 * 60], precision: 6);

            // /IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean
            Assert.Equal(-0.059998, result2[0], precision: 6);
            Assert.Equal( 8.191772, result2[10 * 60], precision: 6);
            Assert.Equal(16.290592, result2[01 * 60 * 60], precision: 6);
            Assert.Equal(15.046221, result2[02 * 60 * 60], precision: 6);
            Assert.Equal(15.274073, result2[10 * 60 * 60], precision: 6);      
        }

        [Fact]
        public async Task CanReadAsStream()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(new ConcurrentDictionary<string, ResourceCatalog>(), default!, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
            var resourcePath = "/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean";
            var catalogItem = (await controller.GetCatalogAsync(InMemory.AccessibleCatalogId, CancellationToken.None)).Find(resourcePath);

            DataSourceController.ChunkSize = 10000;
            var stream = controller.ReadAsStream(begin, end, catalogItem, NullLogger<DataSourceController>.Instance);

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
            Assert.Equal(-0.059998, result[0], precision: 6);
            Assert.Equal(8.191772, result[10 * 60], precision: 6);
            Assert.Equal(16.290592, result[01 * 60 * 60], precision: 6);
            Assert.Equal(15.046221, result[02 * 60 * 60], precision: 6);
            Assert.Equal(15.274073, result[10 * 60 * 60], precision: 6);
        }
    }
}
