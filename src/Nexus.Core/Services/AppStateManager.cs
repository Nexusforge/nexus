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
    internal class AppStateManager
    {
        #region Fields

        private IExtensionHive _extensionHive;
        private ICatalogManager _catalogManager;
        private IDatabaseManager _databaseManager;
        private ILogger<AppStateManager> _logger;
        private SemaphoreSlim _reloadPackagesSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateManager(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager,
            IDatabaseManager databaseManager,
            ILogger<AppStateManager> logger)
        {
            AppState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
            _databaseManager = databaseManager;
            _logger = logger;
        }

        #endregion

        #region Properties

        public AppState AppState { get; }

        #endregion

        #region Methods

        public async Task LoadPackagesAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            await _reloadPackagesSemaphore.WaitAsync();

            try
            {
                var reloadPackagesTask = AppState.ReloadPackagesTask;

                if (reloadPackagesTask is null)
                {
                    /* create fresh app state */
                    AppState.CatalogState = new CatalogState(
                        Root: CatalogContainer.CreateRoot(_catalogManager, _databaseManager),
                        Cache: new CatalogCache()
                    );

                    /* load packages */
                    _logger.LogInformation("Load packages");

                    reloadPackagesTask = _extensionHive
                        .LoadPackagesAsync(AppState.Project.PackageReferences.Values, progress, cancellationToken)
                        .ContinueWith(task =>
                        {
                            this.LoadDataWriters();
                            AppState.ReloadPackagesTask = null;
                            return Task.CompletedTask;
                        }, TaskScheduler.Default);
                }
            }
            finally
            {
                _reloadPackagesSemaphore.Release();
            }
        }

        public async Task PutPackageReferenceAsync(
            Guid packageReferenceId,
            PackageReference packageReference)
        {
            var project = AppState.Project;

            var packageReferences = project.PackageReferences
                .ToDictionary(current => current.Key, current => current.Value);

            packageReferences[packageReferenceId] = packageReference;

            var newProject = project with 
            { 
                PackageReferences = packageReferences
            };

            using var stream = _databaseManager.WriteProject();
            await JsonSerializerHelper.SerializeIntendedAsync(stream, newProject);

            AppState.Project = newProject;
        }

        public async Task DeletePackageReferenceAsync(Guid packageReferenceId)
        {
            var project = AppState.Project;

            var packageReferences = project.PackageReferences
                .ToDictionary(current => current.Key, current => current.Value);

            packageReferences.Remove(packageReferenceId);

            var newProject = project with
            {
                PackageReferences = packageReferences
            };

            using var stream = _databaseManager.WriteProject();
            await JsonSerializerHelper.SerializeIntendedAsync(stream, newProject);

            AppState.Project = newProject;
        }

        private void LoadDataWriters()
        {
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

            AppState.DataWriterInfoMap = dataWriterInfoMap;
        }

        #endregion
    }
}
