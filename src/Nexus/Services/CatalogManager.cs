using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class CatalogManager : ICatalogManager
    {
        #region Events

        public event EventHandler<CatalogState> CatalogStateChanged;

        #endregion

        #region Fields

        private IDataControllerService _dataControllerService;
        private IDatabaseManager _databaseManager;
        private IServiceProvider _serviceProvider;
        private ILogger<CatalogManager> _logger;
        private PathsOptions _options;

        #endregion

        #region Constructors

        public CatalogManager(
            IDataControllerService dataControllerService, 
            IDatabaseManager databaseManager,
            IServiceProvider serviceProvider,
            ILogger<CatalogManager> logger,
            IOptions<PathsOptions> options)
        {
            _dataControllerService = dataControllerService;
            _databaseManager = databaseManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;
        }

        #endregion

        #region Properties

        public CatalogState? State { get; private set; }

        private NexusDatabaseConfig Config { get; set; }

        #endregion

        #region Methods

        public async Task UpdateAsync(CancellationToken cancellationToken)
        {
            FilterDataSource.ClearCache();

            // load data sources and get catalogs
            var backendSourceToCatalogDataMap = (await Task.WhenAll(
                this.Config.BackendSources.Select(async backendSource =>
                    {
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogs = await controller.GetCatalogsAsync(cancellationToken);

                        // ensure that the filter data reader plugin does not create catalogs and resources without permission
                        if (backendSource.Type == FilterDataSource.Id)
                        {
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                                catalogs = this.CleanUpFilterCatalogs(catalogs, userManager);
                            }
                        }

                        // get begin and end of project
                        var catalogData = await Task
                            .WhenAll(catalogs.Select(async catalog => (catalog, await controller.GetTimeRangeAsync(catalog.Id, cancellationToken))));

                        return new KeyValuePair<BackendSource, (ResourceCatalog, TimeRangeResult)[]>(backendSource, catalogData);
                    })))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            // instantiate aggregation data reader
            var backendSource = new BackendSource()
            {
                Type = "Nexus.Aggregation",
                ResourceLocator = new Uri(
                    !string.IsNullOrWhiteSpace(this.Config.AggregationDataReaderRootPath)
                        ? this.Config.AggregationDataReaderRootPath
                        : _options.Cache,
                    UriKind.RelativeOrAbsolute
                )
            };

            using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
            var catalogs = await controller.GetCatalogsAsync(cancellationToken);

            var catalogData = catalogs
                .Select(catalog => (catalog, new TimeRangeResult() { Begin = DateTime.MaxValue, End = DateTime.MinValue, BackendSource = backendSource }))
                .ToArray();

            backendSourceToCatalogDataMap[backendSource] = catalogData;

            // merge all catalogs
            var idToCatalogContainerMap = new Dictionary<string, CatalogContainer>();

            foreach (var entry in backendSourceToCatalogDataMap)
            {
                foreach (var (catalog, timeRangeResult) in entry.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // find catalog container or create a new one
                    if (idToCatalogContainerMap.TryGetValue(catalog.Id, out var container))
                    {
                        // merge time range
                        var begin = container.CatalogBegin;
                        var end = container.CatalogEnd;

                        if (begin == DateTime.MinValue)
                            begin = timeRangeResult.Begin;

                        else
                            begin = new DateTime(Math.Min(begin.Ticks, timeRangeResult.Begin.Ticks));

                        if (end == DateTime.MinValue)
                            end = timeRangeResult.End;

                        else
                            end = new DateTime(Math.Max(end.Ticks, timeRangeResult.End.Ticks));

                        // merge catalog
                        var mergedCatalog = container.Catalog.Merge(catalog, MergeMode.ExclusiveOr);

                        // update catalog container
                        idToCatalogContainerMap[catalog.Id] = container with
                        {
                            CatalogBegin = begin,
                            CatalogEnd = end,
                            Catalog = mergedCatalog
                        };
                    }
                    else
                    {
                        var metadata = default(CatalogMetadata);

                        if (_databaseManager.TryReadCatalogMetadata(catalog.Id, out var stream))
                        {
                            var jsonString = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
                            metadata = JsonSerializerHelper.Deserialize<CatalogMetadata>(jsonString);
                        }
                        else
                        {
                            metadata = new CatalogMetadata();
                        }

                        idToCatalogContainerMap[catalog.Id] = new CatalogContainer(timeRangeResult.Begin, timeRangeResult.End, catalog, metadata);
                    }
                }
            }

            // merge overrides
            foreach (var entry in idToCatalogContainerMap)
            {
                var mergedCatalog = entry.Value.Catalog.Merge(entry.Value.CatalogMetadata.Overrides, MergeMode.NewWins);
                
                idToCatalogContainerMap[entry.Key] = entry.Value with
                {
                    Catalog = mergedCatalog
                };
            }

            // 
            var catalogCollection = new CatalogCollection(idToCatalogContainerMap.Values.ToList());

            var state = new CatalogState()
            {
                AggregationBackendSource = backendSource,
                CatalogCollection = catalogCollection,
                BackendSourceToCatalogsMap = backendSourceToCatalogDataMap.ToDictionary(
                    entry => entry.Key, 
                    entry => entry.Value.Select(current => current.Item1).ToArray()),
            };

            this.CatalogStateChanged?.Invoke(this, state);
            _logger.LogInformation("Catalog state updated.");
        }

        private ResourceCatalog[] CleanUpFilterCatalogs(
            ResourceCatalog[] filterCatalogs,
            UserManager<IdentityUser> userManager)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var filteredCatalogs = new List<ResourceCatalog>();

            foreach (var catalog in filterCatalogs)
            {
                var resources = new List<Resource>();

                foreach (var resource in catalog.Resources)
                {
                    var representations = new List<Representation>();

                    foreach (var representation in resource.Representations)
                    {
                        if (FilterDataSource.TryGetFilterCodeDefinition(resource.Id, representation.BackendSource, out var codeDefinition))
                        {
                            // get user
                            if (!usersMap.TryGetValue(codeDefinition.Owner, out var user))
                            {
                                user = NexusUtilities
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner, userManager)
                                    .Result;

                                usersMap[codeDefinition.Owner] = user;
                            }

                            var keep = catalog.Id == FilterConstants.SharedCatalogID || NexusUtilities.IsCatalogEditable(user, catalog.Id);

                            if (keep)
                                representations.Add(representation);
                        }
                    }

                    if (representations.Any())
                        resources.Add(resource with { Representations = representations });
                }

                if (resources.Any())
                    filteredCatalogs.Add(catalog with { Resources = resources });
            }

            return filteredCatalogs.ToArray();
        }

        #endregion
    }
}
