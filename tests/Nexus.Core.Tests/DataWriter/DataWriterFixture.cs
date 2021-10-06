using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.IO;

namespace DataWriter
{
    public class DataWriterFixture : IDisposable
    {
        List<string> _targetFolders = new List<string>();

        public DataWriterFixture()
        {
            // catalog 1
            var representations1 = new List<Representation>()
            {
                new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "mean", DataType = NexusDataType.FLOAT32 },
                new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "max", DataType = NexusDataType.FLOAT64 },
            };

            var resourceMetadata1 = new Dictionary<string, string>()
            {
                ["my-custom-parameter3"] = "my-custom-value3"
            };

            var resources1 = new List<Resource>()
            {
                new Resource()
                {
                    Id = "resource1",
                    Unit = "°C",
                    Groups = new[] { "group1" },
                    Metadata = resourceMetadata1,
                    Representations = representations1
                }
            };

            var catalog1 = new ResourceCatalog()
            {
                Id = "/A/B/C",
                Resources = resources1
            };

            catalog1.Metadata["my-custom-parameter1"] = "my-custom-value1";
            catalog1.Metadata["my-custom-parameter2"] = "my-custom-value2";

            // catalog 2
            var representations2 = new List<Representation>()
            {
                new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "std", DataType = NexusDataType.INT64 },
            };

            var resources2 = new List<Resource>()
            {
                new Resource()
                {
                    Id = "resource3",
                    Unit = "m/s",
                    Groups = new[] { "group2" },
                    Representations = representations2
                }
            };

            var catalog2 = new ResourceCatalog()
            {
                Id = "/D/E/F",
                Resources = resources2
            };

            catalog2.Metadata["my-custom-parameter3"] = "my-custom-value3";

            this.Catalogs = new[] { catalog1, catalog2 };
        }


        public ResourceCatalog[] Catalogs { get; }

        public string GetTargetFolder()
        {
            var targetFolder = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
            Directory.CreateDirectory(targetFolder);

            _targetFolders.Add(targetFolder);
            return targetFolder;
        }

        public void Dispose()
        {
            foreach (var targetFolder in _targetFolders)
            {
                try
                {
                    Directory.Delete(targetFolder, true);
                }
                catch
                {
                    //
                }
            }
        }       
    }
}
