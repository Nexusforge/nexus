using Microsoft.Extensions.Logging.Abstractions;
using Nexus;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        [Theory]
        [InlineData(AvailabilityGranularity.Day,  "2020-01-01T00:00:00Z", "2020-01-03T00:00:00Z")]
        [InlineData(AvailabilityGranularity.Month, "2020-01-01T00:00:00Z", "2021-01-01T00:00:00Z")]
        public async Task CanGetAvailability(AvailabilityGranularity granularity, string beginString, string endString)
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default, CancellationToken.None);

            var catalogId = (await controller.GetCatalogsAsync(CancellationToken.None)).First().Id;

            var begin = DateTime.ParseExact(
                beginString,
                "yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var end = DateTime.ParseExact(
                endString,
                "yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture, 
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var actual = await controller.GetAvailabilityAsync(catalogId, begin, end, granularity, CancellationToken.None);

            var expectedData = granularity switch
            {
                AvailabilityGranularity.Day => new Dictionary<DateTime, double>() 
                { 
                    [new DateTime(2020, 01, 01)] = 0.90000134808942744,
                    [new DateTime(2020, 01, 02)] = 0.96041512538698282,
                },

                AvailabilityGranularity.Month => new Dictionary<DateTime, double>()
                {
                    [new DateTime(2020, 01, 01)] = 0.90000134808942744,
                    [new DateTime(2020, 02, 01)] = 0.99525375850277664,
                    [new DateTime(2020, 03, 01)] = 0.9800551025104034,
                    [new DateTime(2020, 04, 01)] = 0.96493102473436443,
                    [new DateTime(2020, 05, 01)] = 0.99976965785015826,
                    [new DateTime(2020, 06, 01)] = 0.99502206826350748,
                    [new DateTime(2020, 07, 01)] = 0.92986070137930132,
                    [new DateTime(2020, 08, 01)] = 0.92511311179265066,
                    [new DateTime(2020, 09, 01)] = 0.92036552220599988,
                    [new DateTime(2020, 10, 01)] = 0.9552041553217937,
                    [new DateTime(2020, 11, 01)] = 0.994792088258449,
                    [new DateTime(2020, 12, 01)] = 0.95995345514265518,
                },

            _ => throw new Exception("Unsupported granularity value."),
            };

            Assert.Equal(_fixture.BackendSource, actual.BackendSource);
            Assert.True(expectedData.SequenceEqual(new SortedDictionary<DateTime, double>(actual.Data)));
        }

        [Fact]
        public async Task CanGetTimeRange()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default, CancellationToken.None);

            var catalogId = (await controller.GetCatalogsAsync(CancellationToken.None)).First().Id;
            var actual = await controller.GetTimeRangeAsync(catalogId, CancellationToken.None);

            Assert.Equal(_fixture.BackendSource, actual.BackendSource);
            Assert.Equal(DateTime.MinValue, actual.Begin);
            Assert.Equal(DateTime.MaxValue, actual.End);
        }

        [Fact]
        public async Task CanCheckIsDataOfDayAvailable()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default, CancellationToken.None);

            var day = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var catalogId = (await controller.GetCatalogsAsync(CancellationToken.None)).First().Id;
            var actual = await controller.IsDataOfDayAvailableAsync(catalogId, day, CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task CanRead()
        {
            var controller = _fixture.Controller;
            await controller.InitializeAsync(default, CancellationToken.None);
            
            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromSeconds(1);

            // resource 1
            var resourcePath1 = "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean";
            var catalogItem1 = (await controller.GetCatalogsAsync(CancellationToken.None)).Find(resourcePath1);

            var pipe1 = new Pipe();
            var dataWriter1 = pipe1.Writer;

            // resource 2
            var resourcePath2 = "/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean";
            var catalogItem2 = (await controller.GetCatalogsAsync(CancellationToken.None)).Find(resourcePath2);

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
                NullLogger.Instance,
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
            await controller.InitializeAsync(default, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 1, DateTimeKind.Utc);
            var resourcePath = "/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean";
            var catalogItem = (await controller.GetCatalogsAsync(CancellationToken.None)).Find(resourcePath);

            DataSourceController.ChunkSize = 10000;
            var stream = controller.ReadAsStream(begin, end, catalogItem, NullLogger.Instance);

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
