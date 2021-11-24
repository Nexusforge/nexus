using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    [DebuggerDisplay("{Id,nq}")]
    internal class CatalogContainer
    {
        private SemaphoreSlim _semaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private ICatalogManager _catalogManager;
        private CatalogInfo _catalogInfo;

        public CatalogContainer(
            string catalogId,
            ClaimsPrincipal owner,
            BackendSource backendSource,
            CatalogMetadata catalogMetadata,
            ICatalogManager catalogManager)
        {
            this.Owner = owner;
            this.BackendSource = backendSource;
            this.CatalogMetadata = catalogMetadata;
            _catalogManager = catalogManager;

            this.Id = catalogId;
        }

        public string Id { get; }

        public ClaimsPrincipal Owner { get; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public BackendSource BackendSource { get; }

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
                if (_catalogInfo is null)
                {
                    _catalogInfo = await _catalogManager.LoadCatalogInfoAsync(
                        this.Id,
                        this.BackendSource,
                        this.CatalogMetadata?.Overrides,
                        cancellationToken);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
