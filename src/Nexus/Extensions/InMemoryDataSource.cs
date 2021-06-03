using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.InMemory", "Nexus in-memory", "Provides an in-memory database.")]
    public class InMemoryDataSource : DataReaderExtensionBase
    {
        #region Constructors

        public InMemoryDataSource(DataSourceRegistration registration, ILogger logger) : base(registration, logger)
        {
            //
        }

        #endregion

        #region Methods

        public override (T[] Dataset, byte[] Status) ReadSingle<T>(Dataset dataset, DateTime begin, DateTime end)
        {
            double[] dataDouble;

            var beginTime = begin.ToUnixTimeStamp();
            var endTime = end.ToUnixTimeStamp();

            var length = (int)((end - begin).TotalSeconds * (double)dataset.GetSampleRate().SamplesPerSecond);
            var dt = (double)(1 / dataset.GetSampleRate().SamplesPerSecond);

            if (dataset.Channel.Name.Contains("unix_time"))
            {
                dataDouble = Enumerable.Range(0, length).Select(i => i * dt + beginTime).ToArray();
            }
            else // temperature or wind speed
            {
                var kernelSize = 1000;
                var movingAverage = new double[kernelSize];
                var random = new Random((int)begin.Ticks);
                var mean = 15;

                dataDouble = new double[length];

                for (int i = 0; i < length; i++)
                {
                    movingAverage[i % kernelSize] = (random.NextDouble() - 0.5) * mean * 10 + mean;
                    dataDouble[i] = movingAverage.Sum() / kernelSize;
                }
            }

            var data = dataDouble.Select(value => (T)Convert.ChangeType(value, typeof(T))).ToArray();
            var status = Enumerable.Range(0, length).Select(value => (byte)1).ToArray();

            return (data, status);
        }

        protected override List<Catalog> LoadCatalogs()
        {
            var id11 = Guid.Parse("f01b6a96-1de6-4caa-9205-184d8a3eb2f8");
            var id12 = Guid.Parse("d549a4dd-e003-4d24-98de-4d5bc8c72aca");
            var id13 = Guid.Parse("7dec6d79-b92e-4af2-9358-21be1f3626c9");
            var id14 = Guid.Parse("cf50190b-fd2a-477b-9655-48f4f41ba7bf");
            var catalog_allowed = this.LoadCatalog("/IN_MEMORY/TEST/ACCESSIBLE", id11, id12, id13, id14);

            var id21 = Guid.Parse("50d38fe5-a7a8-49e8-8bd4-3e98a48a951f");
            var id22 = Guid.Parse("d47d1adc6-7c38-4b75-9459-742fa570ef9d");
            var id23 = Guid.Parse("511d6e9c-9075-41ee-bac7-891d359f0dda");
            var id24 = Guid.Parse("99b85689-5373-4a9a-8fd7-be04a89c9da8");
            var catalog_restricted = this.LoadCatalog("/IN_MEMORY/TEST/RESTRICTED", id21, id22, id23, id24);

            return new List<Catalog>() { catalog_allowed, catalog_restricted };
        }

        protected override double GetAvailability(string catalogId, DateTime day)
        {
            if (!this.Catalogs.Any(catalog => catalog.Id == catalogId))
                throw new Exception($"The requested catalog with name '{catalogId}' could not be found.");

            return new Random((int)day.Ticks).NextDouble() / 10 + 0.9;
        }

        private Catalog LoadCatalog(string catalogId, Guid id1, Guid id2, Guid id3, Guid id4)
        {
            var catalog = new Catalog(catalogId);

            var channelA = new Channel(id1, catalog);
            var channelB = new Channel(id2, catalog);
            var channelC = new Channel(id3, catalog);
            var channelD = new Channel(id4, catalog);

            var dataset1 = new Dataset("1 s_mean", channelA) { DataType = NexusDataType.FLOAT64 };
            var dataset2 = new Dataset("1 s_mean", channelB) { DataType = NexusDataType.FLOAT64 };
            var dataset3 = new Dataset("25 Hz", channelC) { DataType = NexusDataType.INT32 };
            var dataset4 = new Dataset("1 s_max", channelD) { DataType = NexusDataType.FLOAT64 };
            var dataset5 = new Dataset("1 s_mean", channelD) { DataType = NexusDataType.FLOAT64 };

            // channel A
            channelA.Name = "T1";
            channelA.Group = "Group 1";
            channelA.Unit = "°C";
            channelA.Description = "Test channel.";

            channelA.Datasets.Add(dataset1);

            // channel B
            channelB.Name = "V1";
            channelB.Group = "Group 1";
            channelB.Unit = "m/s";
            channelB.Description = "Test channel.";

            channelB.Datasets.Add(dataset2);

            // channel C
            channelC.Name = "unix_time1";
            channelC.Group = "Group 2";
            channelC.Unit = "";
            channelC.Description = "Test channel.";

            channelC.Datasets.Add(dataset3);

            // channel D
            channelD.Name = "unix_time2";
            channelD.Group = "Group 2";
            channelD.Unit = string.Empty;
            channelD.Description = "Test channel.";

            channelD.Datasets.Add(dataset4);
            channelD.Datasets.Add(dataset5);

            // catalog
            catalog.Channels = new List<Channel>()
            {
                channelA,
                channelB,
                channelC,
                channelD
            };

            return catalog;
        }

        #endregion
    }
}
