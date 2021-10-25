using Nexus.Core;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class AppStateController
    {
        #region Fields

        private IExtensionHive _extensionHive;
        private ICatalogManager _catalogManager;
        private AppState _appState;
        private SemaphoreSlim _reloadCatalogsSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateController(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager)
        {
            _appState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
        }

        #endregion

        #region Methods

        public async Task ReloadCatalogsAsync(CancellationToken cancellationToken)
        {
            /* check if any work is required */
            var executeReload = false;

            await _reloadCatalogsSemaphore.WaitAsync();

            try
            {
                if (!_appState.IsCatalogStateUpdating)
                {
                    _appState.IsCatalogStateUpdating = true;
                    executeReload = true;
                }
            }
            finally
            {
                _reloadCatalogsSemaphore.Release();
            }

            /* continue */
            if (executeReload)
            {
                try
                {
                    /* await actual update tasks */
                    await _extensionHive.LoadPackagesAsync(_appState.Project.PackageReferences, cancellationToken);
                    _appState.CatalogState = await _catalogManager.LoadCatalogsAsync(cancellationToken);
                }
                finally
                {
                    /* re-enable other tasks to run an update */
                    _appState.IsCatalogStateUpdating = false;
                }
            }
        }

        #endregion
    }
}
