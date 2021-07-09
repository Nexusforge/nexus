using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using Nexus.Utilities;
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

        public Task<List<ResourceCatalog>> GetCatalogsAsync(CancellationToken cancellationToken)
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
                    Catalogs = new List<ResourceCatalog>()
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

        public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var tasks = requests.Select(request =>
            {
                var (resourcePath, data, status) = request;

                return Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (catalog, resource, representation) = ResourceCatalog.Find(resourcePath, this.Context.Catalogs);

                    double[] dataDouble;

                    var beginTime = begin.ToUnixTimeStamp();
                    var endTime = end.ToUnixTimeStamp();

                    var elementCount = data.Length / representation.ElementSize;
                    var dt = representation.GetSamplePeriod().TotalSeconds;

                    if (resource.Name.Contains("unix_time"))
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

                    MemoryMarshal
                        .AsBytes(dataDouble.AsSpan())
                        .CopyTo(data.Span);

                    status.Span
                        .Fill(1);
                });
            });

            return Task.WhenAll(tasks);
        }

        private ResourceCatalog LoadCatalog(string catalogId, Guid id1, Guid id2, Guid id3, Guid id4)
        {
            var catalog = new ResourceCatalog() { Id = catalogId };

            var resourceA = new Resource() { Id = id1, Name = "T1", Group = "Group 1", Unit = "°C" };
            resourceA.Metadata["Description"] = "Test resource.";

            var resourceB = new Resource() { Id = id2, Name = "V1", Group = "Group 1", Unit = "m/s" };
            resourceB.Metadata["Description"] = "Test resource.";

            var resourceC = new Resource() { Id = id3, Name = "unix_time1", Group = "Group 2", Unit = "" };
            resourceC.Metadata["Description"] = "Test resource.";

            var resourceD = new Resource() { Id = id4, Name = "unix_time2", Group = "Group 2", Unit = "" };
            resourceD.Metadata["Description"] = "Test resource.";

            var representation1 = new Representation() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };
            var representation2 = new Representation() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };
            var representation3 = new Representation() { Id = "25 Hz", DataType = NexusDataType.INT32 };
            var representation4 = new Representation() { Id = "1 s_max", DataType = NexusDataType.FLOAT64 };
            var representation5 = new Representation() { Id = "1 s_mean", DataType = NexusDataType.FLOAT64 };

            // resource A
            resourceA.Representations.Add(representation1);
            resourceB.Representations.Add(representation2);
            resourceC.Representations.Add(representation3);
            resourceD.Representations.Add(representation4);
            resourceD.Representations.Add(representation5);

            // catalog
            catalog.Resources.AddRange(new List<Resource>()
            {
                resourceA,
                resourceB,
                resourceC,
                resourceD
            });

            return catalog;
        }

        #endregion
    }
}
