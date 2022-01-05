using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Utilities;
using System;
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
        private IDataControllerService _dataControllerService;
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
                    /* create fresh app state */
                    _appState.CatalogState = new CatalogState(
                        Root: CatalogContainer.CreateRoot(_catalogManager),
                        Cache: new CatalogCache()
                    );

                    /* load packages */
                    _logger.LogInformation("Load packages");
                    await _extensionHive.LoadPackagesAsync(_appState.Project.PackageReferences, cancellationToken);
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
                var fullName = dataWriterType.FullName ?? throw new Exception("full name is null");

                string formatName;

                try
                {
                    formatName = dataWriterType.GetFirstAttribute<DataWriterFormatNameAttribute>().FormatName;
                }
                catch
                {
                    _logger.LogWarning("Data writer {DataWriter} has no format name attribute", fullName);
                    continue;
                }

                var options = dataWriterType
                    .GetCustomAttributes<OptionAttribute>()
                    .ToArray();

                dataWriterInfoMap[fullName] = (formatName, options);
            }

            _appState.DataWriterInfoMap = dataWriterInfoMap;
        }

        #endregion
    }
}
