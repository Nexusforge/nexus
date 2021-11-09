using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class AppStateController
    {
        #region Fields

        private IExtensionHive _extensionHive;
        private ICatalogManager _catalogManager;
        private ILogger<AppStateController> _logger;
        private AppState _appState;
        private SemaphoreSlim _reloadCatalogsSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateController(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager,
            ILogger<AppStateController> logger)
        {
            _appState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
            _logger = logger;
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
                    _logger.LogInformation("Load packages");
                    await _extensionHive.LoadPackagesAsync(_appState.Project.PackageReferences, cancellationToken);

                    _logger.LogInformation("Load catalogs");
                    _appState.CatalogState = await _catalogManager.LoadCatalogsAsync(cancellationToken);
                }
                finally
                {
                    /* re-enable other tasks to run an update */
                    _appState.IsCatalogStateUpdating = false;
                }
            }

            /* update GUI with possible new data writers */
            var dataWriterInfoMap = new Dictionary<string, (string FormatName, OptionAttribute[] Options)>();

            foreach (var dataWriterType in _extensionHive.GetExtensions<IDataWriter>())
            {
                var extensionIdentification = dataWriterType.GetFirstAttribute<ExtensionIdentificationAttribute>();

                string formatName;

                try
                {
                    formatName = dataWriterType.GetFirstAttribute<DataWriterFormatNameAttribute>().FormatName;
                }
                catch
                {
                    _logger.LogWarning("Data writer {DataWriterId} has no format name attribute, skipping", extensionIdentification.Id);
                    continue;
                }

                var options = dataWriterType
                    .GetCustomAttributes<OptionAttribute>()
                    .ToArray();

                dataWriterInfoMap[extensionIdentification.Id] = (formatName, options);
            }

            _appState.DataWriterInfoMap = dataWriterInfoMap;
        }

        #endregion
    }
}
