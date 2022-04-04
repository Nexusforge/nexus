using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using System.Runtime.InteropServices;

namespace Nexus.Sources
{
    [ExtensionDescription("Provides a sample database.")]
    internal class Sample : IDataSource
    {
        #region Fields

        private static double[] DATA = new double[]
        {
            6.5, 6.7, 7.9, 8.1, 7.5, 7.6, 7.0, 6.5, 6.0, 5.9,
            5.8, 5.2, 4.6, 5.0, 5.1, 4.9, 5.3, 5.8, 5.9, 6.1,
            5.9, 6.3, 6.5, 6.9, 7.1, 6.9, 7.1, 7.2, 7.6, 7.9, 
            8.2, 8.1, 8.2, 8.0, 7.5, 7.7, 7.6, 8.0, 7.5, 7.2,
            6.8, 6.5, 6.6, 6.6, 6.7, 6.2, 5.9, 5.7, 5.9, 6.3,
            6.6, 6.7, 6.9, 6.5, 6.0, 5.8, 5.3, 5.8, 6.1, 6.8
        };

        public const string ParentCatalogId = "/SAMPLE";
        public const string AccessibleCatalogId = "/SAMPLE/ACCESSIBLE";
        public const string ForwardedCatalogId = "/SAMPLE/FORWARDED";
        public const string RestrictedCatalogId = "/SAMPLE/RESTRICTED";

        public const string ForwardedUsername = "test";
        public const string ForwardedPassword = "1234";

        #endregion

        #region Properties

        private DataSourceContext Context { get; set; } = null!;

        #endregion

        #region Methods

        public Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.CompletedTask;
        }

        public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
        {
            if (path == "/")
                return Task.FromResult(new CatalogRegistration[] 
                    {
                        new CatalogRegistration(ParentCatalogId),
                    });

            else if (path == ParentCatalogId)
                return Task.FromResult(new CatalogRegistration[]
                    {
                        new CatalogRegistration(AccessibleCatalogId),
                        new CatalogRegistration(ForwardedCatalogId),
                        new CatalogRegistration(RestrictedCatalogId)
                    });

            else
                return Task.FromResult(new CatalogRegistration[0]);
        }

        public Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
        {
            if (catalogId == ParentCatalogId)
                return Task.FromResult(new ResourceCatalog(catalogId));

            else
                return Task.FromResult(Sample.LoadCatalog(catalogId));
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

                    // check credentials
                    if (catalog.Id == ForwardedCatalogId)
                    {
                        if ((Context.Configuration.TryGetValue("user", out var user) && user != ForwardedUsername) ||
                            (Context.Configuration.TryGetValue("password", out var password) && password != ForwardedPassword))
                            throw new Exception("The provided credentials are invalid.");
                    }

                    double[] dataDouble;

                    var beginTime = begin.ToUnixTimeStamp();
                    var endTime = end.ToUnixTimeStamp();

                    var elementCount = data.Length / representation.ElementSize;
                    var dt = representation.SamplePeriod.TotalSeconds;

                    // unit time
                    if (resource.Id.Contains("unix_time"))
                    {
                        dataDouble = Enumerable.Range(0, elementCount).Select(i => i * dt + beginTime).ToArray();
                    }

                    // temperature or wind speed
                    else
                    {
                        var offset = (long)beginTime;
                        var dataLength = DATA.Length;

                        dataDouble = new double[elementCount];

                        for (int i = 0; i < elementCount; i++)
                        {
                            dataDouble[i] = DATA[(offset + i) % dataLength];
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
            var resourceBuilderA = new ResourceBuilder(id: "T1")
                .WithUnit("°C")
                .WithDescription("Test Resource A")
                .WithGroups("Group 1")
                .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

            var resourceBuilderB = new ResourceBuilder(id: "V1")
                .WithUnit("m/s")
                .WithDescription("Test Resource B")
                .WithGroups("Group 1")
                .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

            var resourceBuilderC = new ResourceBuilder(id: "unix_time1")
                .WithDescription("Test Resource C")
                .WithGroups("Group 2")
                .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromMilliseconds(40)));

            var resourceBuilderD = new ResourceBuilder(id: "unix_time2")
                .WithDescription("Test Resource D")
                .WithGroups("Group 2")
                .AddRepresentation(new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)));

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
