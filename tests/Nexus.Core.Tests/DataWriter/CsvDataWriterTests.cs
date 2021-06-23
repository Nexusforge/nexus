using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Core.Tests
{
    public class CsvDataWriterTests
    {
        private ILogger _logger;

        public CsvDataWriterTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(InMemoryDataSourceTests));
        }

        [Fact]
        public void CanWrite()
        {
            var targetFolder = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(targetFolder);

            var dataWriter = new CsvDataWriter()
            {
                TargetFolder = targetFolder,
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
            } as IDataWriter;

            dataWriter.OnParametersSet();

            var begin = new DateTime(01, 01, 2020, 0, 0, 0, DateTimeKind.Utc);

            // datasets
            var datasets = new List<Dataset>()
            {
                new Dataset() { Id = "1 Hz_mean", DataType = NexusDataType.FLOAT32 },
                new Dataset() { Id = "1 Hz_max", DataType = NexusDataType.FLOAT64 },
            };

            // channel
            var channelMetadata = new Dictionary<string, string>()
            {
                ["my-custom-parameter3"] = "my-custom-value3"
            };

            var channels = new List<Channel>()
            {
                new Channel()
                {
                    Id = Guid.NewGuid(),
                    Name = "channel1",
                    Group = "group1",
                    Unit = "°C",
                    Metadata = channelMetadata,
                    Datasets = datasets
                }
            };

            // catalog
            var catalog = new Catalog()
            {
                Id = "/A/B/C",
                Channels = channels
            };

            catalog.Metadata["my-custom-parameter1"] = "my-custom-value1";
            catalog.Metadata["my-custom-parameter2"] = "my-custom-value2";

            var catalogMap = new Dictionary<Catalog, SampleRateContainer>()
            {
                [catalog] = new SampleRateContainer(86400)
            };

            dataWriter.Open(begin, catalogMap);
        }
    }
}