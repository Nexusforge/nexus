using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Models;
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
        private IDatabaseManager _databaseManager;
        private ILogger<AppStateController> _logger;
        private AppState _appState;
        private SemaphoreSlim _reloadPackagesSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateController(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager,
            IDatabaseManager databaseManager,
            ILogger<AppStateController> logger)
        {
            _appState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
            _databaseManager = databaseManager;
            _logger = logger;
        }

        #endregion

        #region Methods

        public async Task ReloadPackagesAsync(CancellationToken cancellationToken)
        {
            /* check if any work is required */
            var executeReload = false;

            await _reloadPackagesSemaphore.WaitAsync();

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
                _reloadPackagesSemaphore.Release();
            }

            /* continue */
            if (executeReload)
            {
                try
                {
                    /* create fresh app state */
                    _appState.CatalogState = new CatalogState(
                        Root: CatalogContainer.CreateRoot(_catalogManager, _databaseManager),
                        Cache: new CatalogCache()
                    );

                    /* load packages */
                    _logger.LogInformation("Load packages");
                    await _extensionHive.LoadPackagesAsync(_appState.Project.PackageReferences.Values, cancellationToken);
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

        public async Task PutPackageReferenceAsync(
            Guid packageReferenceId,
            PackageReference packageReference)
        {
            var project = _appState.Project;

            var packageReferences = project.PackageReferences
                .ToDictionary(current => current.Key, current => current.Value);

            packageReferences[packageReferenceId] = packageReference;

            var newProject = project with 
            { 
                PackageReferences = packageReferences
            };

            using var stream = _databaseManager.WriteProject();
            await JsonSerializerHelper.SerializeIntendedAsync(stream, _appState.Project);

            _appState.Project = newProject;
        }

        public async Task DeletePackageReferenceAsync(Guid packageReferenceId)
        {
            var project = _appState.Project;

            var packageReferences = project.PackageReferences
                .ToDictionary(current => current.Key, current => current.Value);

            packageReferences.Remove(packageReferenceId);

            var newProject = project with
            {
                PackageReferences = packageReferences
            };

            using var stream = _databaseManager.WriteProject();
            await JsonSerializerHelper.SerializeIntendedAsync(stream, _appState.Project);

            _appState.Project = newProject;
        }

        #endregion
    }
}
