using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
using Nexus.Utilities;
using System;
using System.Collections.Concurrent;
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
        private BackendSource _aggregationBackendSource;

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

            _aggregationBackendSource = new BackendSource(
                Type: AggregationDataSource.Id,
                ResourceLocator: new Uri(_options.Cache, UriKind.RelativeOrAbsolute));
        }

        #endregion

        #region Methods

        public async Task<CatalogState> LoadCatalogsAsync(CancellationToken cancellationToken)
        {
            FilterDataSource.ClearCache();

            // prepare built-in backend sources
            var builtinBackendSources = new BackendSource[]
            {
                _aggregationBackendSource,
                new BackendSource(Type: InMemoryDataSource.Id, ResourceLocator: default),
                new BackendSource(Type: FilterDataSource.Id, ResourceLocator: new Uri(_options.Config))
            };

            var extendedBackendSources = builtinBackendSources.Concat(_appState.Project.BackendSources);

            /* load data sources and get catalog ids */
            var backendSourceToCatalogIdsMap = (await Task.WhenAll(
                extendedBackendSources.Select(async backendSource =>
                    {
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogIds = await controller.GetCatalogIdsAsync(cancellationToken);

                        return new KeyValuePair<BackendSource, string[]>(backendSource, catalogIds);
                    })))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            /* inverse the dictionary */
            var flattenedDict = backendSourceToCatalogIdsMap
                .SelectMany(entry => entry.Value.Select(value => new KeyValuePair<BackendSource, string>(entry.Key, value)))
                .ToList();

            var catalogIdToBackendSourceMap = flattenedDict
                .GroupBy(entry => entry.Value)
                .ToDictionary(group => group.Key, group => group.Select(groupEntry => groupEntry.Key).ToArray());

            /* build the catalog containers */
            var catalogContainers = catalogIdToBackendSourceMap
                .Select(entry =>
                {
                    CatalogMetadata catalogMetadata;

                    if (_databaseManager.TryReadCatalogMetadata(entry.Key, out var jsonString))
                        catalogMetadata = JsonSerializerHelper.Deserialize<CatalogMetadata>(jsonString);

                    else
                        catalogMetadata = new CatalogMetadata();

                    return new CatalogContainer(entry.Key, entry.Value, catalogMetadata, this);
                })
                .ToArray();

            var state = new CatalogState(
                AggregationBackendSource: _aggregationBackendSource,
                CatalogContainers: catalogContainers,
                BackendSourceToCatalogIdsMap: backendSourceToCatalogIdsMap,
                BackendSourceCache: new ConcurrentDictionary<BackendSource, ConcurrentDictionary<string, ResourceCatalog>>(
                    backendSourceToCatalogIdsMap
                        .ToDictionary(entry => entry.Key, entry => new ConcurrentDictionary<string, ResourceCatalog>()))
            );

            _logger.LogInformation("Found {CatalogCount} catalogs from {BackendSourceCount} backend sources",
                catalogContainers.Length,
                backendSourceToCatalogIdsMap.Keys.Count);

            return state;
        }

        public async Task<CatalogInfo> LoadCatalogInfoAsync(
            string catalogId, 
            BackendSource[] backendSources,
            ResourceCatalog? catalogOverrides,
            CancellationToken cancellationToken)
        {
            var catalogBegin = default(DateTime);
            var catalogEnd = default(DateTime);
            var catalog = new ResourceCatalog(catalogId);

            foreach (var backendSource in backendSources)
            {
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                var newCatalog = await controller.GetCatalogAsync(catalogId, cancellationToken);

                // ensure that the filter data reader plugin does not create catalogs and resources without permission
                if (backendSource.Type == FilterDataSource.Id)
                    newCatalog = await this.CleanUpFilterCatalogAsync(newCatalog, _userManagerWrapper);

                // get begin and end of project
                TimeRangeResult timeRangeResult;

                if (backendSource.Equals(_aggregationBackendSource))
                    timeRangeResult = new TimeRangeResult(BackendSource: backendSource, Begin: DateTime.MaxValue, End: DateTime.MinValue);

                else
                    timeRangeResult = await controller.GetTimeRangeAsync(newCatalog.Id, cancellationToken);

                // merge time range
                if (catalogBegin == DateTime.MinValue)
                    catalogBegin = timeRangeResult.Begin;

                else
                    catalogBegin = new DateTime(Math.Min(catalogBegin.Ticks, timeRangeResult.Begin.Ticks));

                if (catalogEnd == DateTime.MinValue)
                    catalogEnd = timeRangeResult.End;

                else
                    catalogEnd = new DateTime(Math.Max(catalogEnd.Ticks, timeRangeResult.End.Ticks));

                // merge catalog
                catalog = catalog.Merge(newCatalog, MergeMode.ExclusiveOr);
            }

            if (catalogOverrides is not null)
                catalog = catalog.Merge(catalogOverrides, MergeMode.NewWins);

            return new CatalogInfo(catalogBegin, catalogEnd, catalog);
        }

        private async Task<ResourceCatalog> CleanUpFilterCatalogAsync(
           ResourceCatalog catalog,
           IUserManagerWrapper userManagerWrapper)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var filteredResources = new List<Resource>();

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
                    filteredResources.Add(resource with { Representations = representations });
            }

            return catalog with { Resources = filteredResources };
        }

        #endregion
    }
}
