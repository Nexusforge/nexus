﻿using Nexus.DataModel;
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
    [ExtensionIdentification("Nexus.Builtin.InMemory", "Nexus in-memory", "Provides an in-memory database.")]
    internal class InMemoryDataSource : IDataSource
    {
        #region Fields

        public const string Id = "Nexus.Builtin.InMemory";
        public const string AccessibleCatalogId = "/IN_MEMORY/TEST/ACCESSIBLE";
        public const string RestrictedCatalogId = "/IN_MEMORY/TEST/RESTRICTED";

        #endregion

        #region Properties

        private DataSourceContext Context { get; set; } = null!;

        #endregion

        #region Methods

        public Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            this.Context = context;
            return Task.CompletedTask;
        }

        public Task<string[]> GetCatalogIdsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new string[] { AccessibleCatalogId, RestrictedCatalogId });
        }

        public Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.FromResult(InMemoryDataSource.LoadCatalog(catalogId));
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MinValue, DateTime.MaxValue));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Random((int)begin.Ticks).NextDouble() / 10 + 0.9);
        }

        public async Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
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
            }).ToList();

            var finishedTasks = 0;

            while (tasks.Any())
            {
                var task = await Task.WhenAny(tasks);
                cancellationToken.ThrowIfCancellationRequested();

                if (task.Exception is not null && task.Exception.InnerException is not null)
                    throw task.Exception.InnerException;

                finishedTasks++;
                progress.Report(finishedTasks / (double)requests.Length);
                tasks.Remove(task);
            }
        }

        internal static ResourceCatalog LoadCatalog(string catalogId)
        {
            var representation1 = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1), detail: "mean");
            var representation2 = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1), detail: "mean");
            var representation3 = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromMilliseconds(40), detail: "");
            var representation4 = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1), detail: "max");
            var representation5 = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1), detail: "mean");

            var resourceBuilderA = new ResourceBuilder(id: "T1")
                .WithUnit("°C")
                .WithDescription("Test Resource A")
                .WithGroups("Group 1")
                .AddRepresentation(representation1);

            var resourceBuilderB = new ResourceBuilder(id: "V1")
                .WithUnit("m/s")
                .WithDescription("Test Resource B")
                .WithGroups("Group 1")
                .AddRepresentation(representation2);

            var resourceBuilderC = new ResourceBuilder(id: "unix_time1")
                .WithDescription("Test Resource C")
                .WithGroups("Group 2")
                .AddRepresentation(representation3);

            var resourceBuilderD = new ResourceBuilder(id: "unix_time2")
                .WithDescription("Test Resource D")
                .WithGroups("Group 2")
                .AddRepresentations(representation4, representation5);

            var catalogBuilder = new ResourceCatalogBuilder(catalogId);

            catalogBuilder.AddResources(new List<Resource>()
            {
                resourceBuilderA.Build(),
                resourceBuilderB.Build(),
                resourceBuilderC.Build(),
                resourceBuilderD.Build()
            });

            return catalogBuilder.Build();
        }

        #endregion
    }
}
