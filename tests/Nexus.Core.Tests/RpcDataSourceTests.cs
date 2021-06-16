using Microsoft.Extensions.Logging;
using Moq;
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
            var dataSource = new RpcDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>() 
                { 
                    ["command"] = "python.exe",
                    ["arguments"] =  "PythonRpcDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actual = catalogs.First(catalog => catalog.Id == "/A/B/C");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();

            var expectedNames = new List<string>() { "channel1", "channel2" };
            var expectedGroups = new List<string>() { "group1", "group2" };
            var expectedUnits = new List<string>() { "°C", "bar" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT32, NexusDataType.FLOAT64 };

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

            var dataSource = new RpcDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var actual = await dataSource.GetTimeRangeAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new RpcDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var begin = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(2 / 144.0, actual, precision: 4);
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            var dataSource = new RpcDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            var dataset = catalogs[1].Channels.First().Datasets.First();

            var begin = new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var result = ExtensibilityUtilities.CreateReadResult(dataset, begin, end);

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

            await dataSource.ReadSingleAsync(dataset, result, begin, end, CancellationToken.None);
            var longData = result.GetData<long>();

            Assert.True(expectedData.SequenceEqual(longData.ToArray()));
            Assert.True(expectedStatus.SequenceEqual(result.Status.ToArray()));
        }

        [Fact]
        public async Task CanLog()
        {
            var loggerMock = new Mock<ILogger>();

            var dataSource = new RpcDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "TESTDATA")),
                Logger = loggerMock.Object,
                Parameters = new Dictionary<string, string>()
                {
                    ["command"] = "python.exe",
                    ["arguments"] = "PythonRpcDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

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