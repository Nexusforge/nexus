using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using System.Text.Json;
using Xunit;

namespace DataSource
{
    public class SampleDataSourceTests
    {
        [Fact]
        public async Task ProvidesCatalog()
        {
            // arrange
            var dataSource = new Sample() as IDataSource;

            var context = new DataSourceContext(
                ResourceLocator: new Uri("memory://localhost"),
                SystemConfiguration: default!,
                SourceConfiguration: default!,
                RequestConfiguration: default);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            // act
            var actual = await dataSource.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None);

            // assert
            var actualIds = actual.Resources!.Select(resource => resource.Id).ToList();
            var actualUnits = actual.Resources!.Select(resource => GetPropertyOrDefault(resource.Properties, "unit")).ToList();
            var actualGroups = actual.Resources!.SelectMany(resource => GetArrayOrDefault(resource.Properties, "groups"));
            var actualDataTypes = actual.Resources!.SelectMany(resource => resource.Representations!.Select(representation => representation.DataType)).ToList();

            var expectedIds = new List<string>() { "T1", "V1", "unix_time1", "unix_time2" };
            var expectedUnits = new List<string>() { "°C", "m/s", default!, default! };
            var expectedGroups = new List<string>() { "Group 1", "Group 1", "Group 2", "Group 2" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT64, NexusDataType.FLOAT64, NexusDataType.FLOAT64, NexusDataType.FLOAT64 };

            Assert.True(expectedIds.SequenceEqual(actualIds));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));

            string? GetPropertyOrDefault(JsonElement? element, string propertyName)
            {
                if (!element.HasValue)
                    return default;

                if (element.Value.TryGetProperty(propertyName, out var result))
                    return result.GetString();

                else
                    return default;
            }

            string[] GetArrayOrDefault(JsonElement? element, string propertyName)
            {
                if (!element.HasValue)
                    return new string[0];

                if (element.Value.TryGetProperty(propertyName, out var result))
                    return result.EnumerateArray().Select(current => current.GetString()!).ToArray();

                else
                    return new string[0];
            }
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var dataSource = new Sample() as IDataSource;

            var context = new DataSourceContext(
                ResourceLocator: new Uri("memory://localhost"),
                SystemConfiguration: default!,
                SourceConfiguration: default!,
                RequestConfiguration: default);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/IN_MEMORY/TEST/ACCESSIBLE", CancellationToken.None);

            Assert.Equal(DateTime.MinValue, actual.Begin);
            Assert.Equal(DateTime.MaxValue, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new Sample() as IDataSource;

            var context = new DataSourceContext(
                ResourceLocator: new Uri("memory://localhost"),
                SystemConfiguration: default!,
                SourceConfiguration: default!,
                RequestConfiguration: default);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var begin = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var expected = 1;
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            var dataSource = new Sample() as IDataSource;

            var context = new DataSourceContext(
                ResourceLocator: new Uri("memory://localhost"),
                SystemConfiguration: default!,
                SourceConfiguration: default!,
                RequestConfiguration: default);

            await dataSource.SetContextAsync(context, NullLogger.Instance, CancellationToken.None);

            var catalog = await dataSource.GetCatalogAsync(Sample.LocalCatalogId, CancellationToken.None);
            var resource = catalog.Resources!.First();
            var representation = resource.Representations!.First();
            var catalogItem = new CatalogItem(catalog, resource, representation);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var request = new ReadRequest(catalogItem, data, status);

            await dataSource.ReadAsync(
                begin, 
                end, 
                new[] { request },
                default!,
                new Progress<double>(), 
                CancellationToken.None);

            var doubleData = data.Cast<byte, double>();

            Assert.Equal(6.5, doubleData.Span[0], precision: 1);
            Assert.Equal(7.9, doubleData.Span[29], precision: 1);
            Assert.Equal(6.0, doubleData.Span[54], precision: 1);
        }
    }
}