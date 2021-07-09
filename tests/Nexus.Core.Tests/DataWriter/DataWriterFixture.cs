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

            // representations
            var representations = new List<Representation>()
            {
                new Representation() { Id = "1 Hz_mean", DataType = NexusDataType.FLOAT32 },
                new Representation() { Id = "1 Hz_max", DataType = NexusDataType.FLOAT64 },
            };

            // resource
            var resourceMetadata = new Dictionary<string, string>()
            {
                ["my-custom-parameter3"] = "my-custom-value3"
            };

            var resources = new List<Resource>()
            {
                new Resource()
                {
                    Id = Guid.NewGuid(),
                    Name = "resource1",
                    Group = "group1",
                    Unit = "°C",
                    Metadata = resourceMetadata,
                    Representations = representations
                }
            };

            // catalog
            var catalog = new Catalog()
            {
                Id = "/A/B/C",
                Resources = resources
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
