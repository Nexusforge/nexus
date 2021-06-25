using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.InMemory", "Nexus in-memory", "Provides an in-memory database.")]
    public class InMemoryDataSource : IDataSource
    {
        #region Properties

        private DataSourceContext Context { get; set; }

        #endregion

        #region Methods

        public Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Context = context;
            return Task.CompletedTask;
        }

        public Task<List<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            if (this.Context.Catalogs is null)
            {
                var id11 = Guid.Parse("f01b6a96-1de6-4caa-9205-184d8a3eb2f8");
                var id12 = Guid.Parse("d549a4dd-e003-4d24-98de-4d5bc8c72aca");
                var id13 = Guid.Parse("7dec6d79-b92e-4af2-9358-21be1f3626c9");
                var id14 = Guid.Parse("cf50190b-fd2a-477b-9655-48f4f41ba7bf");
                var catalog_allowed = this.LoadCatalog("/IN_MEMORY/TEST/ACCESSIBLE", id11, id12, id13, id14);

                var id21 = Guid.Parse("50d38fe5-a7a8-49e8-8bd4-3e98a48a951f");
                var id22 = Guid.Parse("d47d1dc6-7c38-4b75-9459-742fa570ef9d");
                var id23 = Guid.Parse("511d6e9c-9075-41ee-bac7-891d359f0dda");
                var id24 = Guid.Parse("99b85689-5373-4a9a-8fd7-be04a89c9da8");
                var catalog_restricted = this.LoadCatalog("/IN_MEMORY/TEST/RESTRICTED", id21, id22, id23, id24);

                this.Context = this.Context with
                {
                    Catalogs = new List<Catalog>()
                    { 
                        catalog_allowed,
                        catalog_restricted 
                    }
                };
            }

            return Task.FromResult(this.Context.Catalogs);
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MinValue, DateTime.MaxValue));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Random((int)begin.Ticks).NextDouble() / 10 + 0.9);
        }

        public Task ReadSingleAsync(string datasetPath, ReadResult result, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var (catalog, channel, dataset) = Catalog.Find(datasetPath, this.Context.Catalogs);

                double[] dataDouble;

                var beginTime = begin.ToUnixTimeStamp();
                var endTime = end.ToUnixTimeStamp();

                var elementCount = result.Data.Length / dataset.ElementSize;
                var dt = (double)(1 / dataset.GetSampleRate().SamplesPerSecond);

                if (channel.Name.Contains("unix_time"))
                {
                    dataDouble = Enumerable.Range(0, elementCount).Select(i => i * dt + beginTime).ToArray();
                }
                else // temperature or wind speed
                {
                    var kernelSize = 1000;
                    var movingAverage = new double[kernelSize];
                    var random = new Random((int)begin.Ticks);
                    var mean = 15;

                    dataDouble = new double[elementCount];

                    for (int i = 0; i < elementCount; i++)
                    {
                        movingAverage[i % kernelSize] = (random.NextDouble() - 0.5) * mean * 10 + mean;
                        dataDouble[i] = movingAverage.Sum() / kernelSize;
                    }
                }

                var data = dataDouble.Select(value => (double)Convert.ChangeType(value, typeof(double))).ToArray();

                MemoryMarshal
                    .AsBytes(data.AsSpan())
                    .CopyTo(result.Data.Span);

                result.Status.Span
                    .Fill(1);
            });
        }

        private Catalog LoadCatalog(string catalogId, Guid id1, Guid id2, Guid id3, Guid id4)
        {
            var catalog = new Catalog() { Id = catalogId };

            var channelA = new Channel() { Id = id1, Name = "T1", Group = "Group 1", Unit = "°C" };
            channelA.Metadata["Description"] = "Test channel.";

            var channelB = new Channel() { Id = id2, Name = "V1", Group = "Group 1", Unit = "m/s" };
            channelB.Metadata["Description"] = "Test channel.";

            var channelC = new Channel() { Id = id3, Name = "unix_time1", Group = "Group 2", Unit = "" };
            channelC.Metadata["Description"] = "Test channel.";

            var channelD = new Channel() { Id = id4, Name = "unix_time2", Group = "Group 2", Unit = "" };
            channelD.Metadata["Description"] = "Test channel.";

            var dataset1 = new Dataset() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };
            var dataset2 = new Dataset() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };
            var dataset3 = new Dataset() { Id = "25 Hz", DataType = NexusDataType.INT32 };
            var dataset4 = new Dataset() { Id = "1 s_max", DataType = NexusDataType.FLOAT64 };
            var dataset5 = new Dataset() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };

            // channel A
            channelA.Datasets.Add(dataset1);
            channelB.Datasets.Add(dataset2);
            channelC.Datasets.Add(dataset3);
            channelD.Datasets.Add(dataset4);
            channelD.Datasets.Add(dataset5);

            // catalog
            catalog.Channels.AddRange(new List<Channel>()
            {
                channelA,
                channelB,
                channelC,
                channelD
            });

            return catalog;
        }

        #endregion
    }
}
