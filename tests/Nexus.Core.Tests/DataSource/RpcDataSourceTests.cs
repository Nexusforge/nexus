using Microsoft.Extensions.Logging;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Core.Tests
{
    public class RpcDataSourceTests
    {
        private ILogger _logger;

        public RpcDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(RpcDataSourceTests));
        }

        [Fact]
        public async Task ProvidesCatalog()
        {
            // arrange
            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>() 
                { 
                    ["command"] = "python.exe",
                    ["arguments"] =  "PythonRpcDataSource.py"
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actualMetadata1 = catalogs.First().Metadata;
            var actual = catalogs.First(catalog => catalog.Id == "/A/B/C");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();
            var actualMetadata2 = actual.Channels.Select(channel => channel.Metadata).ToList();

            var expectedMetadata1 = new Dictionary<string, string>() { ["a"] = "b" };
            var expectedNames = new List<string>() { "channel1", "channel2" };
            var expectedGroups = new List<string>() { "group1", "group2" };
            var expectedUnits = new List<string>() { "°C", "bar" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.INT64, NexusDataType.FLOAT64 };
            var expectedMetadata2 = new List<Dictionary<string, string>>() { new Dictionary<string, string>() { ["c"] = "d" }, new Dictionary<string, string>() };

            Assert.True(actualMetadata1.SequenceEqual(expectedMetadata1));
            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));

            for (int i = 0; i < expectedMetadata2.Count; i++)
            {
                Assert.True(expectedMetadata2[i].SequenceEqual(actualMetadata2[i]));
            }
        }

        [Fact]
        public async Task ProvidesPreloadedCatalog()
        {
            // arrange

            var dataset = new Dataset() { Id = "1 Hz", DataType = NexusDataType.INT32 };

            var channelGuid = Guid.NewGuid();
            var channel = new Channel() { Id = channelGuid, Name = "channel 1", Group = "group 1", Unit = "unit 1" };
            channel.Datasets.Add(dataset);

            var catalog = new Catalog() { Id = "/M/F/G" };
            catalog.Channels.Add(channel);

            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                },
                Logger = _logger,
                Catalogs = new List<Catalog>() { catalog }
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actualMetadata1 = catalogs.First().Metadata;
            var actual = catalogs.First(catalog => catalog.Id == "/M/F/G");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();
            var actualMetadata2 = actual.Channels.Select(channel => channel.Metadata).ToList();

            var expectedNames = new List<string>() { "channel 1" };
            var expectedGroups = new List<string>() { "group 1" };
            var expectedUnits = new List<string>() { "unit 1" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.INT32 };

            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var expectedBegin = new DateTime(2019, 12, 31, 12, 00, 00, DateTimeKind.Utc);
            var expectedEnd = new DateTime(2020, 01, 02, 09, 50, 00, DateTimeKind.Utc);

            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var begin = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(2 / 144.0, actual, precision: 4);
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            var catalog = catalogs.First();
            var channel = catalog.Channels.First();
            var dataset = channel.Datasets.First();
            var datasetPath = new DatasetRecord(catalog, channel, dataset).GetPath();

            var begin = new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(dataset, begin, end);

            var length = 3 * 86400;
            var expectedData = new long[length];
            var expectedStatus = new byte[length];

            void GenerateData(DateTimeOffset dateTime)
            {
                var data = Enumerable.Range(0, 600)
                    .Select(value => dateTime.Add(TimeSpan.FromSeconds(value)).ToUnixTimeSeconds())
                    .ToArray();

                var offset = (int)(dateTime - begin).TotalSeconds;
                data.CopyTo(expectedData.AsSpan().Slice(offset));
                expectedStatus.AsSpan().Slice(offset, 600).Fill(1);
            }

            GenerateData(new DateTimeOffset(2019, 12, 31, 12, 00, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2019, 12, 31, 12, 20, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 01, 00, 00, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 02, 09, 40, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 02, 09, 50, 0, 0, TimeSpan.Zero));

            var request = new ReadRequest(datasetPath, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { request }, CancellationToken.None);
            var longData = data.Cast<long>();

            Assert.True(expectedData.SequenceEqual(longData.ToArray()));
            Assert.True(expectedStatus.SequenceEqual(status.ToArray()));
        }

        [Fact]
        public async Task CanLog()
        {
            var loggerMock = new Mock<ILogger>();
            var dataSource = new RpcDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Configuration = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                },
                Logger = loggerMock.Object,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((message, _) => message.ToString() == "Logging works!"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)
                ),
                Times.Once
            );
        }
    }
}