using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class PipeDataSourceTests
    {
        [Fact]
        public async Task ProvidesCatalog()
        {
            // arrange
            var dataSource = new PipeDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "DATABASES/TESTDATA")),
                Logger = NullLogger.Instance,
                Parameters = new Dictionary<string, string>() 
                { 
                    ["command"] = "python.exe",
                    ["arguments"] =  "PythonPipeDataSource.py"
                }
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            var actual = catalogs.First(catalog => catalog.Id == "/A/B/C");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();
            //var actualTimeRange = await dataSource.GetCatalogTimeRangeAsync("/A/B/C", CancellationToken.None);

            // assert
            var expectedNames = new List<string>() { "channel1", "channel2" };
            var expectedGroups = new List<string>() { "group1", "group2" };
            var expectedUnits = new List<string>() { "°C", "bar" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT32, NexusDataType.FLOAT64 };

#warning Implement this
            //var expectedStartDate = new DateTime(2020, 10, 05, 12, 00, 00, DateTimeKind.Utc);
            //var expectedEndDate = new DateTime(2020, 10, 05, 14, 00, 00, DateTimeKind.Utc);

            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));
            //Assert.Equal(expectedStartDate, actualTimeRange.Begin);
            //Assert.Equal(expectedEndDate, actualTimeRange.End);
        }

        [Fact]
        public async Task ProvidesDataAvailability()
        {
            //// arrange
            //var dataSource = new CampbellDataSource()
            //{
            //    ResourceLocator = new Uri("CampbellTestDatabase", UriKind.Relative),
            //    Logger = NullLogger.Instance
            //} as IDataSource;

            //await dataSource.OnParametersSetAsync();

            //// act
            //var actual = new Dictionary<DateTime, double>();
            //var begin = new DateTime(2020, 10, 04, 0, 0, 0, DateTimeKind.Utc);
            //var end = new DateTime(2020, 10, 06, 0, 0, 0, DateTimeKind.Utc);

            //var currentBegin = begin;

            //while (currentBegin < end)
            //{
            //    actual[currentBegin] = await dataSource.GetAvailabilityAsync("/A/B/C", currentBegin, currentBegin.AddDays(1), CancellationToken.None);
            //    currentBegin += TimeSpan.FromDays(1);
            //}

            //// assert
            //var expected = new SortedDictionary<DateTime, double>(Enumerable.Range(0, 2).ToDictionary(
            //        i => begin.AddDays(i),
            //        i => 0.0));

            //expected[begin.AddDays(0)] = 0;
            //expected[begin.AddDays(1)] = 4 / 48.0;

            //Assert.True(expected.SequenceEqual(new SortedDictionary<DateTime, double>(actual)));
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            //// arrange
            //var dataSource = new CampbellDataSource()
            //{
            //    ResourceLocator = new Uri("CampbellTestDatabase", UriKind.Relative),
            //    Logger = NullLogger.Instance
            //} as IDataSource;

            //await dataSource.OnParametersSetAsync();

            //// act
            //var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
            //var dataset = catalog.Channels.First().Datasets.First();

            //var begin = new DateTime(2020, 10, 05, 0, 0, 0, DateTimeKind.Utc);
            //var end = new DateTime(2020, 10, 06, 0, 0, 0, DateTimeKind.Utc);

            //using var result = ExtensibilityUtilities.CreateReadResult<float>(dataset, begin, end);
            //await dataSource.ReadSingleAsync(dataset, result, begin, end, CancellationToken.None);

            //// assert
            //Assert.Equal(0, result.Data.Span[864000 - 1]);
            //Assert.Equal(8.590, result.Data.Span[864000 + 0], precision: 3);
            //Assert.Equal(6.506, result.Data.Span[1008000 - 1], precision: 3);
            //Assert.Equal(0, result.Data.Span[1008000 + 0]);

            //Assert.Equal(0, result.Status.Span[864000 - 1]);
            //Assert.Equal(1, result.Status.Span[864000 + 0]);
            //Assert.Equal(1, result.Status.Span[1008000 - 1]);
            //Assert.Equal(0, result.Status.Span[1008000 + 0]);
        }
    }
}