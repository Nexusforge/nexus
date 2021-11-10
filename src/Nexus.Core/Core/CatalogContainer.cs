using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        private Task<CatalogInfo>? _loadTask;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private BackendSource[] _backendSources;
        private ICatalogManager _catalogManager;
        private CatalogInfo _catalogInfo;

        public CatalogContainer(
            string catalogId,
            BackendSource[] backendSources,
            CatalogMetadata catalogMetadata,
            ICatalogManager catalogManager)
        {
            _backendSources = backendSources;
            this.CatalogMetadata = catalogMetadata;
            _catalogManager = catalogManager;

            this.Id = catalogId;
        }

        public string Id { get; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public CatalogMetadata CatalogMetadata { get; }

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
                if (_loadTask is null)
                    _loadTask = _catalogManager.LoadCatalogInfoAsync(
                        this.Id, 
                        _backendSources,
                        this.CatalogMetadata?.Overrides,
                        cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }

            _catalogInfo = await _loadTask;
        }
    }
}
