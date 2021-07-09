using Microsoft.Extensions.Logging;
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
    public class CsvDataWriterTests : IClassFixture<DataWriterFixture>
    {
        private DataWriterFixture _fixture;
        private ILogger _logger;

        public CsvDataWriterTests(DataWriterFixture fixture, ITestOutputHelper xunitLogger)
        {
            _fixture = fixture;
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(InMemoryDataSourceTests));
        }

        [Fact]
        public async Task CanWrite()
        {
            var dataWriter = new CsvDataWriter() as IDataWriter;

            var context = new DataWriterContext()
            {
                ResourceLocator = new Uri(_fixture.TargetFolder),
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
                {
                    ["RowIndexFormat"] = "Index"
                }
            };

            await dataWriter.SetContextAsync(context, CancellationToken.None);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var samplePeriod = TimeSpan.FromSeconds(1);

            var representationRecords = _fixture.Catalog.Resources
                .SelectMany(resource => resource.Representations.Select(representation => new RepresentationRecord(_fixture.Catalog, resource, representation)))
                .ToArray();

            var representationRecordGroups = new[]
            {
                new RepresentationRecordGroup(_fixture.Catalog, "My License", representationRecords)
            };

            var requests = representationRecords
                .Select(representationRecord => new WriteRequest(representationRecord, new double[1000]))
                .ToArray();

            var requestGroups = new[]
            {
                new WriteRequestGroup(_fixture.Catalog, requests)
            };

            await dataWriter.OpenAsync(begin, TimeSpan.FromSeconds(1), representationRecordGroups, CancellationToken.None);
            await dataWriter.WriteAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1), requestGroups, new Progress<double>(), CancellationToken.None);
            await dataWriter.CloseAsync(CancellationToken.None);

            var actualFilePath = Directory.GetFiles(_fixture.TargetFolder).SingleOrDefault();

            Assert.Equal("A_B_C_2020-01-01T00-00-00Z_1_s.csv", Path.GetFileName(actualFilePath));
        }
    }
}