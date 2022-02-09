using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics;
using System.Security.Claims;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private CatalogInfo? _catalogInfo;
        private CatalogContainer[]? _childCatalogContainers;
        private ICatalogManager _catalogManager;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;

        public CatalogContainer(
            CatalogRegistration registration,
            ClaimsPrincipal owner,
            BackendSource backendSource,
            CatalogMetadata metadata,
            ICatalogManager catalogManager,
            IDatabaseManager databaseManager,
            IDataControllerService dataControllerService)
        {
            this.Id = registration.Path;
            this.IsTransient = registration.IsTransient;
            this.Owner = owner;
            this.BackendSource = backendSource;
            this.Metadata = metadata;

            _catalogManager = catalogManager;
            _databaseManager = databaseManager;
            _dataControllerService = dataControllerService;
        }

        public string Id { get; }

        public bool IsTransient { get; }

        public ClaimsPrincipal Owner { get; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public BackendSource BackendSource { get; }

        public CatalogMetadata Metadata { get; internal set; }

        public static CatalogContainer CreateRoot(ICatalogManager catalogManager, IDatabaseManager databaseManager)
        {
            return new CatalogContainer(new CatalogRegistration("/"), null!, null!, null!, catalogManager, databaseManager, null!);
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
            await _semaphore.WaitAsync();

            try
            {
                await this.EnsureCatalogInfoAsync(cancellationToken);

                var catalogInfo = _catalogInfo;

                if (catalogInfo is null)
                    throw new Exception("this should never happen");

                return catalogInfo;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateMetadataAsync(CatalogMetadata metadata)
        {
            await _semaphore.WaitAsync();

            try
            {
                // persist
                using var stream = _databaseManager.WriteCatalogMetadata(this.Id);
                await JsonSerializerHelper.SerializeIntendedAsync(stream, metadata);

                // assign
                this.Metadata = metadata;

                // trigger merging of catalog and catalog overrides
                _catalogInfo = null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task EnsureCatalogInfoAsync(CancellationToken cancellationToken)
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
                    catalog = catalog.Merge(this.Metadata.Overrides, MergeMode.NewWins);

                _catalogInfo = new CatalogInfo(catalogBegin, catalogEnd, catalog);
            }
        }
    }
}
