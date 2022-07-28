using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Utilities;
using System.Reflection;
using System.Text.Json;

namespace Nexus.Services
{
    internal class AppStateManager
    {
        #region Fields

        private IExtensionHive _extensionHive;
        private ICatalogManager _catalogManager;
        private IDatabaseService _databaseService;
        private ILogger<AppStateManager> _logger;
        private SemaphoreSlim _refreshDatabaseSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        private SemaphoreSlim _projectSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        #endregion

        #region Constructors

        public AppStateManager(
            AppState appState,
            IExtensionHive extensionHive,
            ICatalogManager catalogManager,
            IDatabaseService databaseService,
            ILogger<AppStateManager> logger)
        {
            AppState = appState;
            _extensionHive = extensionHive;
            _catalogManager = catalogManager;
            _databaseService = databaseService;
            _logger = logger;
        }

        #endregion

        #region Properties

        public AppState AppState { get; }

        #endregion

        #region Methods

        public async Task RefreshDatabaseAsync(
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            await _refreshDatabaseSemaphore.WaitAsync();

            try
            {
#warning make atomic
                var refreshDatabaseTask = AppState.ReloadPackagesTask;

                if (refreshDatabaseTask is null)
                {
                    /* create fresh app state */
                    AppState.CatalogState = new CatalogState(
                        Root: CatalogContainer.CreateRoot(_catalogManager, _databaseService),
                        Cache: new CatalogCache()
                    );

                    /* load packages */
                    _logger.LogInformation("Load packages");

                    refreshDatabaseTask = _extensionHive
                        .LoadPackagesAsync(AppState.Project.PackageReferences.Values, progress, cancellationToken)
                        .ContinueWith(task =>
                        {
                            LoadDataWriters();
                            AppState.ReloadPackagesTask = default;
                            return Task.CompletedTask;
                        }, TaskScheduler.Default);
                }
            }
            finally
            {
                _refreshDatabaseSemaphore.Release();
            }
        }

        public async Task PutPackageReferenceAsync(
            PackageReference packageReference)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                var newPackageReferences = project.PackageReferences
                    .ToDictionary(current => current.Key, current => current.Value);

                newPackageReferences[packageReference.Id] = packageReference;

                var newProject = project with
                {
                    PackageReferences = newPackageReferences
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task DeletePackageReferenceAsync(
            Guid packageReferenceId)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                var newPackageReferences = project.PackageReferences
                    .ToDictionary(current => current.Key, current => current.Value);

                newPackageReferences.Remove(packageReferenceId);

                var newProject = project with
                {
                    PackageReferences = newPackageReferences
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task PutDataSourceRegistrationAsync(string username, DataSourceRegistration registration)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                if (!project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                    userConfiguration = new UserConfiguration(new Dictionary<Guid, DataSourceRegistration>());

                var newDataSourceRegistrations = userConfiguration.DataSourceRegistrations
                    .ToDictionary(current => current.Key, current => current.Value);

                newDataSourceRegistrations[registration.Id] = registration;

                var newUserConfiguration = userConfiguration with
                {
                    DataSourceRegistrations = newDataSourceRegistrations
                };

                var userConfigurations = project.UserConfigurations
                    .ToDictionary(current => current.Key, current => current.Value);

                userConfigurations[username] = newUserConfiguration;

                var newProject = project with
                {
                    UserConfigurations = userConfigurations
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task DeleteDataSourceRegistrationAsync(string username, Guid registrationId)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                if (!project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                    return;

                var newDataSourceRegistrations = userConfiguration.DataSourceRegistrations
                    .ToDictionary(current => current.Key, current => current.Value);

                newDataSourceRegistrations.Remove(registrationId);

                var newUserConfiguration = userConfiguration with
                {
                    DataSourceRegistrations = newDataSourceRegistrations
                };

                var userConfigurations = project.UserConfigurations
                    .ToDictionary(current => current.Key, current => current.Value);

                userConfigurations[username] = newUserConfiguration;

                var newProject = project with
                {
                    UserConfigurations = userConfigurations
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        public async Task PutSystemConfigurationAsync(JsonElement? configuration)
        {
            await _projectSemaphore.WaitAsync();

            try
            {
                var project = AppState.Project;

                var newProject = project with
                {
                    SystemConfiguration = configuration
                };

                await SaveProjectAsync(newProject);

                AppState.Project = newProject;
            }
            finally
            {
                _projectSemaphore.Release();
            }
        }

        private void LoadDataWriters()
        {
            var dataWriterDescriptions = new List<ExtensionDescription>();

            /* for each data writer */
            foreach (var dataWriterType in _extensionHive.GetExtensions<IDataWriter>())
            {
                var fullName = dataWriterType.FullName!;
                var attribute = dataWriterType.GetCustomAttribute<DataWriterDescriptionAttribute>();

                if (attribute is null)
                {
                    _logger.LogWarning("Data writer {DataWriter} has no description attribute", fullName);
                    continue;
                }

                var additionalInformation = attribute.Description;

                if (!(additionalInformation.ValueKind == JsonValueKind.Object && 
                      additionalInformation.TryGetProperty("label", out var labelProperty) && 
                      labelProperty.ValueKind == JsonValueKind.String))
                    throw new Exception($"The description of data writer {fullName} has no label property");

                var attribute2 = dataWriterType.GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                if (attribute2 is null)
                    dataWriterDescriptions.Add(new ExtensionDescription(
                        fullName, 
                        default, 
                        default,
                        default, 
                        additionalInformation));

                else
                    dataWriterDescriptions.Add(new ExtensionDescription(
                        fullName, 
                        attribute2.Description, 
                        attribute2.ProjectUrl, 
                        attribute2.RepositoryUrl, 
                        additionalInformation));
            }

            AppState.DataWriterDescriptions = dataWriterDescriptions;
        }

        private async Task SaveProjectAsync(NexusProject project)
        {
            using var stream = _databaseService.WriteProject();
            await JsonSerializerHelper.SerializeIntendedAsync(stream, project);
        }

        #endregion
    }
}
