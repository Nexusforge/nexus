using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
                .Select(entry => new CatalogContainer(entry.Key, aggregationBackendSource, entry.Value, _databaseManager, _dataControllerService, _userManagerWrapper))
                .ToArray();

            var state = new CatalogState(
                AggregationBackendSource: aggregationBackendSource,
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

        #endregion
    }
}
