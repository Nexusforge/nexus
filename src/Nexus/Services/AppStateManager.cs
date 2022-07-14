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
        private SemaphoreSlim _reloadPackagesSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
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

        public async Task LoadPackagesAsync(
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            await _reloadPackagesSemaphore.WaitAsync();

            try
            {
                var reloadPackagesTask = AppState.ReloadPackagesTask;

                if (reloadPackagesTask is null)
                {
                    /* create fresh app state */
                    AppState.CatalogState = new CatalogState(
                        Root: CatalogContainer.CreateRoot(_catalogManager, _databaseService),
                        Cache: new CatalogCache()
                    );

                    /* load packages */
                    _logger.LogInformation("Load packages");

                    reloadPackagesTask = _extensionHive
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
                _reloadPackagesSemaphore.Release();
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
            const string OPTIONS_KEY = "UI:Options";
            const string FORMAT_NAME_KEY = "UI:FormatName";
            const string TYPE_KEY = "Type";

            var dataWriterDescriptions = new List<ExtensionDescription>();

            /* for each data writer */
            foreach (var dataWriterType in _extensionHive.GetExtensions<IDataWriter>())
            {
                var fullName = dataWriterType.FullName!;
                var additionalInfo = new Dictionary<string, string>();

                /* format name */
                try
                {
                    additionalInfo[FORMAT_NAME_KEY] = dataWriterType.GetFirstAttribute<DataWriterFormatNameAttribute>().FormatName;
                }
                catch
                {
                    _logger.LogWarning("Data writer {DataWriter} has no format name attribute", fullName);
                    continue;
                }

                var counter = 0;

                /* for each option */
                foreach (var option in dataWriterType.GetCustomAttributes<OptionAttribute>())
                {
                    additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(option.ConfigurationKey)}"] = option.ConfigurationKey;
                    additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(option.Label)}"] = option.Label;

                    if (option is DataWriterIntegerNumberInputOptionAttribute integerNumberInput)
                    {
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{TYPE_KEY}"] = "IntegerNumberInput";
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(integerNumberInput.DefaultValue)}"] = integerNumberInput.DefaultValue.ToString();
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(integerNumberInput.Minmum)}"] = integerNumberInput.Minmum.ToString();
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(integerNumberInput.Maximum)}"] = integerNumberInput.Maximum.ToString();
                    }

                    else if (option is DataWriterSelectOptionAttribute select)
                    {
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{TYPE_KEY}"] = "Select";
                        additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(select.DefaultValue)}"] = select.DefaultValue.ToString();

                        var counter2 = 0;

                        foreach (var entry in select.KeyValueMap)
                        {
                            additionalInfo[$"{OPTIONS_KEY}:{counter}:{nameof(select.KeyValueMap)}:{counter2}:{entry.Key}"] = entry.Value;
                            counter2++;
                        }
                    }

                    counter++;
                }

                var attribute = dataWriterType.GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                if (attribute is null)
                    dataWriterDescriptions.Add(new ExtensionDescription(
                        fullName, 
                        default, 
                        default,
                        default, 
                        additionalInfo));

                else
                    dataWriterDescriptions.Add(new ExtensionDescription(
                        fullName, 
                        attribute.Description, 
                        attribute.ProjectUrl, 
                        attribute.RepositoryUrl, 
                        additionalInfo));
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
