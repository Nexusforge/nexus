using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus;
using Nexus.Core.Tests;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace DataSource
{
    public class AggregationDataSourceTests : IClassFixture<AggregationDataSourceFixture>
    {
        private AggregationDataSourceFixture _fixture;

        public AggregationDataSourceTests(AggregationDataSourceFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ProvidesCatalog()
        {
            var dataSource = new AggregationDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = _fixture.ResourceLocator,
                Configuration = new Dictionary<string, string>(),
                Logger = NullLogger.Instance
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var actual = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);

            // assert
            Assert.Single(actual.Resources);
            Assert.Equal("10_ms_mean", actual.Resources.First().Representations.First().Id);
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var expectedBegin = new DateTime(2020, 07, 08, 00, 00, 00, DateTimeKind.Utc);
            var expectedEnd = new DateTime(2020, 07, 09, 00, 00, 00, DateTimeKind.Utc);

            var dataSource = new AggregationDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = _fixture.ResourceLocator,
                Configuration = new Dictionary<string, string>(),
                Logger = NullLogger.Instance
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new AggregationDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = _fixture.ResourceLocator,
                Configuration = new Dictionary<string, string>(),
                Logger = NullLogger.Instance
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var begin = new DateTime(2020, 07, 07, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 07, 10, 0, 0, 0, DateTimeKind.Utc);
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(2.0 / 3.0, actual);
        }

        [Fact]
        public async Task CanReadTwoDaysShifted()
        {
            // arrange
            var dataSource = new AggregationDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = _fixture.ResourceLocator,
                Configuration = new Dictionary<string, string>(),
                Logger = NullLogger.Instance
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var catalog = await dataSource.GetCatalogAsync("/A/B/C", CancellationToken.None);
            var resource = catalog.Resources.First();
            var representation = resource.Representations.First();
            var catalogItem = new CatalogItem(catalog, resource, representation);

            var begin = new DateTime(2020, 07, 07, 23, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 07, 10, 00, 00, 00, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var result = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new ReadRequest[] { result }, new Progress<double>(), CancellationToken.None);
            var doubleData = data.Cast<byte, double>();

            // assert
            var samplesPerDay = 86400 * 100;

            var baseOffset = samplesPerDay / 24 * 1;
            var dayOffset = 86400 * 100;
            var hourOffset = 360000;
            var halfHourOffset = hourOffset / 2;

            // day 1
            Assert.Equal(0, result.Status.Span[baseOffset - 1]);
            Assert.Equal(1, result.Status.Span[baseOffset + 0]);
            Assert.Equal(1, result.Status.Span[baseOffset + 86400 * 100 - 1]);
            Assert.Equal(99.27636, doubleData.Span[baseOffset + 0], precision: 5);
            Assert.Equal(double.NaN, doubleData.Span[baseOffset + 1]);
            Assert.Equal(99.27626, doubleData.Span[baseOffset + 2], precision: 5);
            Assert.Equal(2323e-3, doubleData.Span[baseOffset + 86400 * 100 - 1]);

            // day 2
            Assert.Equal(1, result.Status.Span[baseOffset + dayOffset + 0]);
            Assert.Equal(1, result.Status.Span[baseOffset + dayOffset + dayOffset - hourOffset - 1]);
            Assert.Equal(98.27636, doubleData.Span[baseOffset + dayOffset + 0], precision: 5);
            Assert.Equal(97.27626, doubleData.Span[baseOffset + dayOffset + 2], precision: 5);
            Assert.Equal(2323e-6, doubleData.Span[baseOffset + dayOffset + dayOffset - hourOffset - 1]);

            Assert.Equal(1, result.Status.Span[baseOffset + dayOffset + dayOffset - halfHourOffset + 0]);
            Assert.Equal(1, result.Status.Span[baseOffset + dayOffset + dayOffset - 1]);
            Assert.Equal(90.27636, doubleData.Span[baseOffset + dayOffset + dayOffset - halfHourOffset + 0], precision: 5);
            Assert.Equal(90.27626, doubleData.Span[baseOffset + dayOffset + dayOffset - halfHourOffset + 2], precision: 5);
            Assert.Equal(2323e-9, doubleData.Span[baseOffset + dayOffset + dayOffset - 1]);
        }
    }
}