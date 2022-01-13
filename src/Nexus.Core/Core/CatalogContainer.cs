using Nexus.DataModel;
using Nexus.Models;
using Nexus.Services;
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
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private CatalogInfo? _catalogInfo;
        private CatalogContainer[]? _childCatalogContainers;
        private ICatalogManager _catalogManager;
        private IDataControllerService _dataControllerService;

        public CatalogContainer(
            CatalogRegistration registration,
            ClaimsPrincipal owner,
            BackendSource backendSource,
            CatalogMetadata metadata,
            ICatalogManager catalogManager,
            IDataControllerService dataControllerService)
        {
            this.Id = registration.Path;
            this.IsTransient = registration.IsTransient;
            this.Owner = owner;
            this.BackendSource = backendSource;
            this.Metadata = metadata;

            _catalogManager = catalogManager;
            _dataControllerService = dataControllerService;
        }

        public string Id { get; }

        public bool IsTransient { get; }

        public ClaimsPrincipal Owner { get; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public BackendSource BackendSource { get; }

        public CatalogMetadata Metadata { get; }

        public static CatalogContainer CreateRoot(ICatalogManager catalogManager)
        {
            return new CatalogContainer(new CatalogRegistration("/"), null!, null!, null!, catalogManager, null!);
        }

        public async Task<IEnumerable<CatalogContainer>> GetChildCatalogContainersAsync(
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (this.IsTransient || _childCatalogContainers is null)
                    _childCatalogContainers = await _catalogManager.GetCatalogContainersAsync(this, cancellationToken);

                return _childCatalogContainers;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<CatalogInfo> GetCatalogInfoAsync(CancellationToken cancellationToken)
        {
            await this.EnsureLoadedAsync(cancellationToken);
            return _catalogInfo;
        }

        private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync();

            try
            {
                if (this.IsTransient || _catalogInfo is null)
                {
                    var catalogBegin = default(DateTime);
                    var catalogEnd = default(DateTime);

                    using var controller = await _dataControllerService.GetDataSourceControllerAsync(this.BackendSource, cancellationToken);
                    var catalog = await controller.GetCatalogAsync(this.Id, cancellationToken);

                    // get begin and end of project
                    var timeRangeResult = await controller.GetTimeRangeAsync(catalog.Id, cancellationToken);

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
                    if (this.Metadata?.Overrides is not null)
                        catalog = catalog.Merge(this.Metadata?.Overrides, MergeMode.NewWins);

                    _catalogInfo = new CatalogInfo(catalogBegin, catalogEnd, catalog);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
