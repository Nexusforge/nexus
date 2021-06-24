using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void CanWrite()
        {
            var dataWriter = new CsvDataWriter()
            {
                TargetFolder = _fixture.TargetFolder,
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
                {
                    ["RowIndexFormat"] = "Index"
                }
            } as IDataWriter;

            dataWriter.OnParametersSet();

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);

            var catalogMap = new Dictionary<Catalog, SampleRateContainer>()
            {
                [_fixture.Catalog] = new SampleRateContainer(86400)
            };

            dataWriter.Open(begin, catalogMap);

            var actualFilePath = Directory.GetFiles(_fixture.TargetFolder).SingleOrDefault();

            Assert.Equal("A_B_C_2020-01-01T00-00-00Z_1_s.csv",  Path.GetFileName(actualFilePath));
        }
    }
}