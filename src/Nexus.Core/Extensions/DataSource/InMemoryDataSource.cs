using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    [ExtensionIdentification("Nexus.Builtin.Inmemory", "Nexus in-memory", "Provides an in-memory database.")]
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

        public Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            if (this.Context.Catalogs is null)
            {
                var catalog_allowed = this.LoadCatalog("/IN_MEMORY/TEST/ACCESSIBLE");
                var catalog_restricted = this.LoadCatalog("/IN_MEMORY/TEST/RESTRICTED");

                this.Context = this.Context with
                {
                    Catalogs = new[]
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
                var (catalogItem, data, status) = request;

                return Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (catalog, resource, representation) = catalogItem;

                    double[] dataDouble;

                    var beginTime = begin.ToUnixTimeStamp();
                    var endTime = end.ToUnixTimeStamp();

                    var elementCount = data.Length / representation.ElementSize;
                    var dt = representation.SamplePeriod.TotalSeconds;

                    if (resource.Id.Contains("unix_time"))
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

        private ResourceCatalog LoadCatalog(string catalogId)
        {
            var catalog = new ResourceCatalog() { Id = catalogId };

            var resourceA = new Resource() { Id = "T1", Unit = "°C", Groups = new[] { "Group 1" } };
            resourceA.Metadata["Description"] = "Test resource.";

            var resourceB = new Resource() { Id = "V1", Unit = "m/s", Groups = new[] { "Group 1" } };
            resourceB.Metadata["Description"] = "Test resource.";

            var resourceC = new Resource() { Id = "unix_time1", Unit = "", Groups = new[] { "Group 2" } };
            resourceC.Metadata["Description"] = "Test resource.";

            var resourceD = new Resource() { Id = "unix_time2", Unit = "", Groups = new[] { "Group 2" } };
            resourceD.Metadata["Description"] = "Test resource.";

            var representation1 = new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "mean", DataType = NexusDataType.FLOAT64 };
            var representation2 = new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "mean", DataType = NexusDataType.FLOAT64 };
            var representation3 = new Representation() { SamplePeriod = TimeSpan.FromMilliseconds(40), Detail = "", DataType = NexusDataType.INT32 };
            var representation4 = new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "max", DataType = NexusDataType.FLOAT64 };
            var representation5 = new Representation() { SamplePeriod = TimeSpan.FromSeconds(1), Detail = "mean", DataType = NexusDataType.FLOAT64 };

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
