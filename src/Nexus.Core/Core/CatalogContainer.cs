using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        private Task _loader;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private BackendSource _aggregationBackendSource;
        private BackendSource[] _backendSources;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;
        private IUserManagerWrapper _userManagerWrapper;

        private DateTime _catalogBegin = DateTime.MinValue;
        private DateTime _catalogEnd = DateTime.MinValue;
        private ResourceCatalog _catalog;

        public CatalogContainer(
            string catalogId,
            BackendSource aggregationBackendSource,
            BackendSource[] backendSources,
            IDatabaseManager databaseManager,
            IDataControllerService dataControllerService,
            IUserManagerWrapper userManagerWrapper)
        {
            _aggregationBackendSource = aggregationBackendSource;
            _backendSources = backendSources;
            _databaseManager = databaseManager;
            _dataControllerService = dataControllerService;
            _userManagerWrapper = userManagerWrapper;

            _catalog = new ResourceCatalog(catalogId);

            if (!backendSources.Any())
                throw new Exception("At least a single backend source must be provided.");

            this.Id = catalogId;

            if (_databaseManager.TryReadCatalogMetadata(this.Id, out var jsonString))
                this.CatalogMetadata = JsonSerializerHelper.Deserialize<CatalogMetadata>(jsonString);

            else
                this.CatalogMetadata = new CatalogMetadata();
        }

        public string Id { get; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public CatalogMetadata CatalogMetadata { get; }

        public async Task<DateTime> GetCatalogBeginAsync(CancellationToken cancellationToken)
        {
            await this.EnsureLoadedAsync(cancellationToken);
            return _catalogBegin;
        }

        public async Task<DateTime> GetCatalogEndAsync(CancellationToken cancellationToken)
        {
            await this.EnsureLoadedAsync(cancellationToken);
            return _catalogEnd;
        }

        public async Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken)
        {
            await this.EnsureLoadedAsync(cancellationToken);
            return _catalog;
        }

        private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (_loader is null)
                    _loader = this.LoadAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }

            await _loader;
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            foreach (var backendSource in _backendSources)
            {
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                var catalog = await controller.GetCatalogAsync(this.Id, cancellationToken);

                // ensure that the filter data reader plugin does not create catalogs and resources without permission
                if (backendSource.Type == FilterDataSource.Id)
                    catalog = await this.CleanUpFilterCatalogAsync(catalog, _userManagerWrapper);

                // get begin and end of project
                TimeRangeResult timeRangeResult;

                if (backendSource.Equals(_aggregationBackendSource))
                    timeRangeResult = new TimeRangeResult(BackendSource: backendSource, Begin: DateTime.MaxValue, End: DateTime.MinValue);

                else
                    timeRangeResult = await controller.GetTimeRangeAsync(catalog.Id, cancellationToken);

                // merge time range
                if (_catalogBegin == DateTime.MinValue)
                    _catalogBegin = timeRangeResult.Begin;

                else
                    _catalogBegin = new DateTime(Math.Min(_catalogBegin.Ticks, timeRangeResult.Begin.Ticks));

                if (_catalogEnd == DateTime.MinValue)
                    _catalogEnd = timeRangeResult.End;

                else
                    _catalogEnd = new DateTime(Math.Max(_catalogEnd.Ticks, timeRangeResult.End.Ticks));

                // merge catalog
                _catalog = _catalog.Merge(catalog, MergeMode.ExclusiveOr);
            }

            if (this.CatalogMetadata.Overrides is not null)
                _catalog = _catalog.Merge(this.CatalogMetadata.Overrides, MergeMode.NewWins);
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
