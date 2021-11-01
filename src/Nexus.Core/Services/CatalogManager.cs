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
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class CatalogManager : ICatalogManager
    {
        #region Fields

        private AppState _appState;
        private IDataControllerService _dataControllerService;
        private IDatabaseManager _databaseManager;
        private IUserManagerWrapper _userManagerWrapper;
        private ILogger<CatalogManager> _logger;
        private PathsOptions _options;

        #endregion

        #region Constructors

        public CatalogManager(
            AppState appState,
            IDataControllerService dataControllerService, 
            IDatabaseManager databaseManager,
            IUserManagerWrapper userManagerWrapper,
            ILogger<CatalogManager> logger,
            IOptions<PathsOptions> options)
        {
            _appState = appState;
            _dataControllerService = dataControllerService;
            _databaseManager = databaseManager;
            _userManagerWrapper = userManagerWrapper;
            _logger = logger;
            _options = options.Value;
        }

        #endregion

        #region Methods

        public async Task<CatalogState> LoadCatalogsAsync(CancellationToken cancellationToken)
        {
            FilterDataSource.ClearCache();

            // prepare built-in backend sources
            var aggregationBackendSource = new BackendSource(
                Type: AggregationDataSource.Id,
                ResourceLocator: new Uri(_options.Cache, UriKind.RelativeOrAbsolute));

            var builtinBackendSources = new BackendSource[]
            {
                aggregationBackendSource,
                new BackendSource(Type: InMemoryDataSource.Id, ResourceLocator: default),
                new BackendSource(Type: FilterDataSource.Id, ResourceLocator: new Uri(_options.Config))
            };

            var extendedBackendSources = builtinBackendSources.Concat(_appState.Project.BackendSources);

            // load data sources and get catalogs
            var backendSourceToCatalogDataMap = (await Task.WhenAll(
                extendedBackendSources.Select(async backendSource =>
                    {
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogs = await controller.GetCatalogsAsync(cancellationToken);

                        // ensure that the filter data reader plugin does not create catalogs and resources without permission
                        if (backendSource.Type == FilterDataSource.Id)
                            catalogs = await this.CleanUpFilterCatalogsAsync(catalogs, _userManagerWrapper);

                        // get begin and end of project
                        (ResourceCatalog, TimeRangeResult)[] catalogData;

                        if (backendSource.Equals(aggregationBackendSource))
                            catalogData = catalogs
                                .Select(catalog => (catalog, new TimeRangeResult(BackendSource: backendSource, Begin: DateTime.MaxValue, End: DateTime.MinValue)))
                                .ToArray();

                        else
                            catalogData = await Task
                                .WhenAll(catalogs.Select(async catalog => (catalog, await controller.GetTimeRangeAsync(catalog.Id, cancellationToken))));

                        return new KeyValuePair<BackendSource, (ResourceCatalog, TimeRangeResult)[]>(backendSource, catalogData);
                    })))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

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

                        if (_databaseManager.TryReadCatalogMetadata(catalog.Id, out var catalogMetadata))
                            metadata = JsonSerializerHelper.Deserialize<CatalogMetadata>(catalogMetadata);

                        else
                            metadata = new CatalogMetadata();

                        idToCatalogContainerMap[catalog.Id] = new CatalogContainer(timeRangeResult.Begin, timeRangeResult.End, catalog, metadata);
                    }
                }
            }

            // merge overrides
            foreach (var entry in idToCatalogContainerMap)
            {
                var mergedCatalog = entry.Value.CatalogMetadata.Overrides is null 
                    ? entry.Value.Catalog
                    : entry.Value.Catalog.Merge(entry.Value.CatalogMetadata.Overrides, MergeMode.NewWins);
                
                idToCatalogContainerMap[entry.Key] = entry.Value with
                {
                    Catalog = mergedCatalog
                };
            }

            // 
            var catalogCollection = new CatalogCollection(idToCatalogContainerMap.Values.ToList());

            var state = new CatalogState(
                AggregationBackendSource: aggregationBackendSource,
                CatalogCollection: catalogCollection,
                BackendSourceToCatalogsMap: backendSourceToCatalogDataMap.ToDictionary(
                    entry => entry.Key, 
                    entry => entry.Value.Select(current => current.Item1).ToArray())
            );

            _logger.LogInformation("Loaded {CatalogCount} catalogs from {BackendSourceCount} backend sources.",
                catalogCollection.CatalogContainers.Count,
                backendSourceToCatalogDataMap.Keys.Count);

            return state;
        }

        private async Task<ResourceCatalog[]> CleanUpFilterCatalogsAsync(
            ResourceCatalog[] filterCatalogs,
            IUserManagerWrapper userManagerWrapper)
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
                                user = await userManagerWrapper
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner);

                                usersMap[codeDefinition.Owner] = user;
                            }

                            var keep = catalog.Id == FilterConstants.SharedCatalogID || AuthorizationUtilities.IsCatalogEditable(user, catalog.Id);

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
