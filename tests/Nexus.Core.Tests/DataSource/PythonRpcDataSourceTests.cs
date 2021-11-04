using Microsoft.Extensions.Logging;
using Moq;
using Nexus;
using Nexus.Core.Tests;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace DataSource
{
    public class PythonRpcDataSourceTests
    {
        private ILogger _logger;

        public PythonRpcDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(PythonRpcDataSourceTests));
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
                    ["arguments"] = "PythonRpcDataSource.py 44444",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "44444",
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var actual = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);

            // assert
            var actualProperties1 = actual.Properties;
            var actualIds = actual.Resources.Select(resource => resource.Id).ToList();
            var actualUnits = actual.Resources.Select(resource => resource.Properties["Unit"]).ToList();
            var actualGroups = actual.Resources.SelectMany(
                resource => resource.Properties.Where(current => current.Key.StartsWith("Groups"))).Select(current => current.Value).ToList();
            var actualDataTypes = actual.Resources.SelectMany(resource => resource.Representations.Select(representation => representation.DataType)).ToList();

            var expectedProperties1 = new Dictionary<string, string>() { ["a"] = "b" };
            var expectedIds = new List<string>() { "resource1", "resource2" };
            var expectedUnits = new List<string>() { "°C", "bar" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.INT64, NexusDataType.FLOAT64 };
            var expectedGroups = new List<string>() { "group1", "group2" };

            Assert.True(actualProperties1.SequenceEqual(expectedProperties1));
            Assert.True(expectedIds.SequenceEqual(actualIds));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
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
                    ["arguments"] = "PythonRpcDataSource.py 44444",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "44444",
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
                    ["arguments"] = "PythonRpcDataSource.py 44444",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "44444",
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
                    ["arguments"] = "PythonRpcDataSource.py 44444",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "44444",
                },
                Logger = _logger,
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
            var resource = catalog.Resources.First();
            var representation = resource.Representations.First();
            var catalogItem = new CatalogItem(catalog, resource, representation);

            var begin = new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

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

            var request = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { request }, new Progress<double>(), CancellationToken.None);
            var longData = data.Cast<byte, long>();

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
                    ["arguments"] = "PythonRpcDataSource.py 44444",
                    ["listen-address"] = "127.0.0.1",
                    ["listen-port"] = "44444",
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