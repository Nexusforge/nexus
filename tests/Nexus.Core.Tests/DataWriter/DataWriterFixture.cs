using Nexus.DataModel;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Core.Tests
{
    public class DataWriterFixture : IDisposable
    {
        public DataWriterFixture()
        {
            this.TargetFolder = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(this.TargetFolder);

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

            this.Catalog = catalog;
        }

        public string TargetFolder { get; }

        public Catalog Catalog { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.TargetFolder, true);
            }
            catch
            {
                //
            }
        }       
    }
}
