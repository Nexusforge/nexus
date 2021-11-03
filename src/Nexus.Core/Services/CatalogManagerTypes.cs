using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal record CatalogState(
        BackendSource AggregationBackendSource,
        CatalogCollection CatalogCollection,
        Dictionary<BackendSource, ConcurrentDictionary<string, ResourceCatalog>> BackendSourceToCatalogsMap);

    internal class LazyCatalogLoader
    {
        private Lazy<Task<CatalogContainer>> _loader;
        private CatalogMetadata _catalogMetadata;
        private BackendSource _aggregationBackendSource;
        private BackendSource[] _backendSources;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;
        private IUserManagerWrapper _userManagerWrapper;
        private CancellationToken _cancellationToken;

        public LazyCatalogLoader(
            string catalogId, 
            CatalogMetadata catalogMetadata,
            BackendSource aggregationBackendSource,
            BackendSource[] backendSources,
            IDatabaseManager databaseManager,
            IDataControllerService dataControllerService,
            IUserManagerWrapper userManagerWrapper)
        {
            _catalogMetadata = catalogMetadata;
            _aggregationBackendSource = aggregationBackendSource;
            _backendSources = backendSources;
            _databaseManager = databaseManager;
            _dataControllerService = dataControllerService;
            _userManagerWrapper = userManagerWrapper;

#pragma warning disable VSTHRD011 // AsyncLazy<T> verwenden
            _loader = new Lazy<Task<CatalogContainer>>(LoadAsync);
#pragma warning restore VSTHRD011 // AsyncLazy<T> verwenden

            if (!backendSources.Any())
                throw new Exception("At least a single backend source must be provided.");

            this.CatalogId = catalogId;
        }

        public string CatalogId { get; }

        public Task<CatalogContainer> GetCatalogContainerAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            return _loader.Value;
        }

        private async Task<CatalogContainer> LoadAsync()
        {
            var container = default(CatalogContainer);

            foreach (var backendSource in _backendSources)
            {
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, _cancellationToken);
                var catalog = await controller.GetCatalogAsync(this.CatalogId, _cancellationToken);

                // ensure that the filter data reader plugin does not create catalogs and resources without permission
                if (backendSource.Type == FilterDataSource.Id)
                    catalog = await this.CleanUpFilterCatalogAsync(catalog, _userManagerWrapper);

                // get begin and end of project
                TimeRangeResult timeRangeResult;

                if (backendSource.Equals(_aggregationBackendSource))
                    timeRangeResult = new TimeRangeResult(BackendSource: backendSource, Begin: DateTime.MaxValue, End: DateTime.MinValue);

                else
                    timeRangeResult = await controller.GetTimeRangeAsync(catalog.Id, _cancellationToken);

                // update catalog container
                if (container is not null)
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
                    container = container with
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

                    container = new CatalogContainer(timeRangeResult.Begin, timeRangeResult.End, catalog, metadata);
                }
            }

            if (_catalogMetadata.Overrides is null)
                return container;

            else
                return container with 
                { 
                    Catalog = container.Catalog.Merge(_catalogMetadata.Overrides, MergeMode.NewWins) 
                };
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
    }
}
