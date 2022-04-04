using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            var begin= new DateTime(2020, 01, 01, 00, 00, 00, DateTimeKind.Utc);
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
                new GeneralOptions() { ReadChunkSize = 20000 },
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
                new GeneralOptions() { ReadChunkSize = 10000 },
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
    }
}
