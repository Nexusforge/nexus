using Microsoft.Extensions.Logging;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Tests
{
    public class AggregationDataReaderTests : IClassFixture<AggregationDataReaderFixture>
    {
        private ILogger _logger;
        private AggregationDataReaderFixture _fixture;

        public AggregationDataReaderTests(AggregationDataReaderFixture fixture, ITestOutputHelper xunitLogger)
        {
            _fixture = fixture;
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(AggregationDataReaderTests));
        }

        [Fact]
        public void ProvidesProjectIds()
        {
            // arrange
            var dataReader = new AggregationDataReader(_fixture.DataReaderRegistration, _logger);
            dataReader.InitializeProjects();

            // act
            var actual = dataReader.GetProjectIds();
            actual.Sort();

            // assert
            var expected = new List<string>() { "/A/B/C", "/A2/B/C" };
            Assert.True(expected.SequenceEqual(actual));
        }

        [Fact]
        public void ProvidesProject()
        {
            // arrange
            var dataReader = new AggregationDataReader(_fixture.DataReaderRegistration, _logger);
            dataReader.InitializeProjects();

            // act
            var actual = dataReader.GetProject("/A/B/C");

            // assert
            var expectedStartDate = new DateTime(2020, 07, 08, 00, 00, 00);
            var expectedEndDate = new DateTime(2020, 07, 09, 00, 00, 00);

            Assert.Single(actual.Channels);
            Assert.Equal("100 Hz_mean", actual.Channels.First().Datasets.First().Id);
            Assert.Equal(expectedStartDate, actual.ProjectStart);
            Assert.Equal(expectedEndDate, actual.ProjectEnd);
        }

        [Fact]
        public void ProvidesAvailability()
        {
            // arrange
            var dataReader = new AggregationDataReader(_fixture.DataReaderRegistration, _logger);
            dataReader.InitializeProjects();

            // act
            var actual = dataReader.GetAvailability("/A/B/C", new DateTime(2020, 07, 07, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 07, 10, 0, 0, 0, DateTimeKind.Utc), Database.AvailabilityGranularity.Day);

            // assert
            var expected = new SortedDictionary<DateTime, double>(new Dictionary<DateTime, double>
            {
                [new DateTime(2020, 07, 07)] = 0.0,
                [new DateTime(2020, 07, 08)] = 1.0,
                [new DateTime(2020, 07, 09)] = 1.0
            });

            Assert.True(expected.SequenceEqual(new SortedDictionary<DateTime, double>(actual.Data)));
        }

        [Fact]
        public void CanReadTwoDaysShifted()
        {
            // arrange
            var dataReader = new AggregationDataReader(_fixture.DataReaderRegistration, _logger);
            dataReader.InitializeProjects();

            // act
            var project = dataReader.GetProject("/A/B/C");
            var dataset = project.Channels.First().Datasets.First();

            var begin = new DateTime(2020, 07, 07, 23, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 07, 10, 00, 00, 00, DateTimeKind.Utc);

            var result = dataReader.ReadSingle<double>(dataset, begin, end);

            // assert
            var samplesPerDay = 86400 * 100;

            var baseOffset = samplesPerDay / 24 * 1;
            var dayOffset = 86400 * 100;
            var hourOffset = 360000;
            var halfHourOffset = hourOffset / 2;

            // day 1
            Assert.Equal(0, result.Status[baseOffset - 1]);
            Assert.Equal(1, result.Status[baseOffset + 0]);
            Assert.Equal(1, result.Status[baseOffset + 86400 * 100 - 1]);
            Assert.Equal(99.27636, result.Dataset[baseOffset + 0], precision: 5);
            Assert.Equal(double.NaN, result.Dataset[baseOffset + 1]);
            Assert.Equal(99.27626, result.Dataset[baseOffset + 2], precision: 5);
            Assert.Equal(2323e-3, result.Dataset[baseOffset + 86400 * 100 - 1]);

            // day 2
            Assert.Equal(1, result.Status[baseOffset + dayOffset + 0]);
            Assert.Equal(1, result.Status[baseOffset + dayOffset + dayOffset - hourOffset - 1]);
            Assert.Equal(98.27636, result.Dataset[baseOffset + dayOffset + 0], precision: 5);
            Assert.Equal(97.27626, result.Dataset[baseOffset + dayOffset + 2], precision: 5);
            Assert.Equal(2323e-6, result.Dataset[baseOffset + dayOffset + dayOffset - hourOffset - 1]);

            Assert.Equal(1, result.Status[baseOffset + dayOffset + dayOffset - halfHourOffset + 0]);
            Assert.Equal(1, result.Status[baseOffset + dayOffset + dayOffset - 1]);
            Assert.Equal(90.27636, result.Dataset[baseOffset + dayOffset + dayOffset - halfHourOffset + 0], precision: 5);
            Assert.Equal(90.27626, result.Dataset[baseOffset + dayOffset + dayOffset - halfHourOffset + 2], precision: 5);
            Assert.Equal(2323e-9, result.Dataset[baseOffset + dayOffset + dayOffset - 1]);
        }
    }
}