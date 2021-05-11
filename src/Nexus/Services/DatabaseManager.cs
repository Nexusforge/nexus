using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Database;
using Nexus.Core;
using Nexus.Filters;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    /* 
     * Algorithm:
     * *******************************************************************************
     * 01. load database.json (this.Database)
     * 02. load and instantiate data reader extensions (_rootPathToDataReaderMap)
     * 03. call Update() method
     * 04.   for each data reader in _rootPathToDataReaderMap
     * 05.       get project names
     * 06.           for each project name
     * 07.               find project container in current database or create new one
     * 08.               get an up-to-date project instance from the data reader
     * 09.               merge both projects
     * 10. save updated database
     * *******************************************************************************
     */

    public class DatabaseManager
    {
        #region Events

        public event EventHandler<NexusDatabase> DatabaseUpdated;

        #endregion

        #region Records

        public record DatabaseManagerState
        {
            public DataReaderRegistration AggregationRegistration { get; init; }
            public NexusDatabase Database { get; init; }
            public Dictionary<DataReaderRegistration, Type> RegistrationToDataReaderTypeMap { get; init; }
            public Dictionary<DataReaderRegistration, List<ProjectInfo>> RegistrationToProjectsMap { get; init; }
        }

        #endregion

        #region Fields

        private NexusOptions _options;

        private bool _isInitialized;
        private ILogger<DatabaseManager>  _logger;
        private ILoggerFactory _loggerFactory;
        private IServiceProvider _serviceProvider;

        #endregion

        #region Constructors

        public DatabaseManager(IServiceProvider serviceProvider, ILogger<DatabaseManager> logger, ILoggerFactory loggerFactory, NexusOptions options)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _options = options;
        }

        #endregion

        #region Properties

        public NexusDatabase Database => this.State?.Database;

        public NexusDatabaseConfig Config { get; private set; }

        public DatabaseManagerState State { get; private set; }

        #endregion

        #region Methods

        public Task UpdateAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                this.Update(cancellationToken);
            });
        }

        public void Update(CancellationToken cancellationToken)
        {
            if (!_isInitialized)
                this.Initialize();

            // Concept:
            //
            // 1) registrationToProjectsMap, registrationToDataReaderTypeMap and database are instantiated in this method,
            // combined into a new DatabaseManagerState and then set in an atomic operation to the State propery.
            // 
            // 2) Within this method, the registrationToProjectsMap cache gets filled
            //
            // 3) It may happen that during this process, which might take a while, an external caller calls 
            // GetDataReader. To divide both processes (external call vs this method),the State property is introduced, 
            // so external calls use old maps and this method uses the new instances.

            FilterDataReader.ClearCache();
            var database = new NexusDatabase();

            // create new empty projects map
            var registrationToProjectsMap = new Dictionary<DataReaderRegistration, List<ProjectInfo>>();

            // load data readers
            var registrationToDataReaderTypeMap = this.LoadDataReaders(this.Config.DataReaderRegistrations);

            // register aggregation data reader
            var registration = new DataReaderRegistration()
            {
                DataReaderId = "Nexus.Aggregation",
                RootPath = !string.IsNullOrWhiteSpace(this.Config.AggregationDataReaderRootPath) 
                    ? this.Config.AggregationDataReaderRootPath
                    : _options.DataBaseFolderPath
            };

            registrationToDataReaderTypeMap[registration] = typeof(AggregationDataReader);

            // instantiate data readers
            var dataReaders = registrationToDataReaderTypeMap
                                .Select(entry => this.InstantiateDataReader(entry.Key, entry.Value, registrationToProjectsMap))
                                .ToList();

            foreach (var dataReader in dataReaders)
            {
                try
                {
                    dataReader.Dispose();
                }
                catch { }
            }

            // get project meta data
            var projectMetas = registrationToProjectsMap
                .SelectMany(entry => entry.Value)
                .Select(project => project.Id)
                .Distinct()
                .Select(projectId =>
                {
                    var filePath = this.GetProjectMetaPath(projectId);

                    if (File.Exists(filePath))
                    {
                        var jsonString = File.ReadAllText(filePath);
                        return JsonSerializer.Deserialize<ProjectMeta>(jsonString);
                    }
                    else
                    {
                        return new ProjectMeta(projectId);
                    }
                })
                .ToList();

            // ensure that the filter data reader plugin does not create projects and channels without permission
            var filterProjects = registrationToProjectsMap
                .Where(entry => entry.Key.DataReaderId == FilterDataReader.Id)
                .SelectMany(entry => entry.Value)
                .ToList();

            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                this.CleanUpFilterProjects(filterProjects, projectMetas, userManager, cancellationToken);
            }

            // merge all projects
            foreach (var entry in registrationToProjectsMap)
            {
                foreach (var project in entry.Value)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    // find project container or create a new one
                    var container = database.ProjectContainers.FirstOrDefault(container => container.Id == project.Id);

                    if (container == null)
                    {
                        var projectMeta = projectMetas.First(projectMeta => projectMeta.Id == project.Id);

                        container = new ProjectContainer(project.Id);
                        container.ProjectMeta = projectMeta;
                        database.ProjectContainers.Add(container);
                    }

                    container.Project.Merge(project, ChannelMergeMode.OverwriteMissing);
                }
            }

            // the purpose of this block is to initalize empty properties,
            // add missing channels and clean up empty channels
            foreach (var projectContainer in database.ProjectContainers)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // remove all channels where no native datasets are available
                // because only these provide metadata like name and group
                var channels = projectContainer.Project.Channels;

                channels
                    .Where(channel => string.IsNullOrWhiteSpace(channel.Name))
                    .ToList()
                    .ForEach(channel => channels.Remove(channel));

                // initalize project meta
                projectContainer.ProjectMeta.Initialize(projectContainer.Project);

                // save project meta to disk
                this.SaveProjectMeta(projectContainer.ProjectMeta);
            }

            this.State = new DatabaseManagerState()
            {
                AggregationRegistration = registration,
                Database = database,
                RegistrationToProjectsMap = registrationToProjectsMap,
                RegistrationToDataReaderTypeMap = registrationToDataReaderTypeMap
            };

            this.DatabaseUpdated?.Invoke(this, database);
            _logger.LogInformation("Database loaded.");
        }

        public List<DataReaderExtensionBase> GetDataReaders(ClaimsPrincipal user, string projectId)
        {
            var state = this.State;

            return state.RegistrationToProjectsMap
                // where the project list contains the project ID
                .Where(entry => entry.Value.Any(project => project.Id == projectId))
                // select the registration and get a brand new data reader from it
                .Select(entry => this.GetDataReader(user, entry.Key, state))
                // to list
                .ToList();
        }

        public DataReaderExtensionBase GetDataReader(
                    ClaimsPrincipal user,
                    DataReaderRegistration registration,
                    DatabaseManagerState state = null)
        {
            if (state == null)
                state = this.State;

            if (!state.RegistrationToDataReaderTypeMap.TryGetValue(registration, out var dataReaderType))
                throw new KeyNotFoundException("The requested data reader could not be found.");

            var dataReader = this.InstantiateDataReader(registration, dataReaderType, state.RegistrationToProjectsMap);

            // special case checks
            if (dataReaderType == typeof(FilterDataReader))
            {
                ((FilterDataReader)dataReader).User = user;
                ((FilterDataReader)dataReader).DatabaseManager = this;
            }

            return dataReader;
        }

        public void SaveProjectMeta(ProjectMeta projectMeta)
        {
            var filePath = this.GetProjectMetaPath(projectMeta.Id);
            var jsonString = JsonSerializer.Serialize(projectMeta, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText(filePath, jsonString);
        }

        public void SaveConfig(string folderPath, NexusDatabaseConfig config)
        {
            var filePath = Path.Combine(folderPath, "dbconfig.json");
            var jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions() { WriteIndented = true });

            File.WriteAllText(filePath, jsonString);
        }

        private void Initialize()
        {
            var dbFolderPath = _options.DataBaseFolderPath;

            if (string.IsNullOrWhiteSpace(dbFolderPath))
            {
                throw new Exception("Could not initialize database. Please check the database folder path and try again.");
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(dbFolderPath);
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "ATTACHMENTS"));
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "DATA"));
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "EXPORT"));
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "EXTENSION"));
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "META"));
                    Directory.CreateDirectory(Path.Combine(dbFolderPath, "PRESETS"));

                    var filePath = Path.Combine(dbFolderPath, "dbconfig.json");

                    if (File.Exists(filePath))
                    {
                        var jsonString = File.ReadAllText(filePath);
                        this.Config = JsonSerializer.Deserialize<NexusDatabaseConfig>(jsonString);
                    }
                    else
                    {
                        this.Config = new NexusDatabaseConfig();
                    }

                    // extend config with more data readers
                    var inMemoryRegistration = new DataReaderRegistration()
                    {
                        DataReaderId = "Nexus.InMemory",
                        RootPath = ":memory:"
                    };

                    if (!this.Config.DataReaderRegistrations.Contains(inMemoryRegistration))
                        this.Config.DataReaderRegistrations.Add(inMemoryRegistration);

                    var filterRegistration = new DataReaderRegistration()
                    {
                        DataReaderId = "Nexus.Filters",
                        RootPath = _options.DataBaseFolderPath
                    };

                    if (!this.Config.DataReaderRegistrations.Contains(filterRegistration))
                        this.Config.DataReaderRegistrations.Add(filterRegistration);

                    // save config to disk
                    this.SaveConfig(dbFolderPath, this.Config);
                }
                catch
                {
                    throw new Exception("Could not initialize database. Please check the database folder path and try again.");
                }
            }

            _isInitialized = true;
        }

        private DataReaderExtensionBase InstantiateDataReader(
            DataReaderRegistration registration, Type type,
            Dictionary<DataReaderRegistration, List<ProjectInfo>> registrationToProjectsMap)
        {
            var logger = _loggerFactory.CreateLogger($"{registration.DataReaderId} - {registration.RootPath}");
            var dataReader = (DataReaderExtensionBase)Activator.CreateInstance(type, registration, logger);

            // special case checks
            if (type == typeof(AggregationDataReader))
            {
                var fileAccessManger = _serviceProvider.GetRequiredService<FileAccessManager>();
                ((AggregationDataReader)dataReader).FileAccessManager = fileAccessManger;
            }

            // initialize projects property
            if (registrationToProjectsMap.TryGetValue(registration, out var value))
            {
                dataReader.InitializeProjects(value);
            }
            else
            {
                _logger.LogInformation($"Loading {registration.DataReaderId} on path {registration.RootPath} ...");
                dataReader.InitializeProjects();

                registrationToProjectsMap[registration] = dataReader
                    .Projects
                    .Where(current => NexusUtilities.CheckProjectNamingConvention(current.Id, out var _))
                    .ToList();
            }

            return dataReader;
        }

        private string GetProjectMetaPath(string projectName)
        {
            return Path.Combine(_options.DataBaseFolderPath, "META", $"{projectName.TrimStart('/').Replace('/', '_')}.json");
        }

        private Dictionary<DataReaderRegistration, Type> LoadDataReaders(List<DataReaderRegistration> dataReaderRegistrations)
        {
            var extensionDirectoryPath = Path.Combine(_options.DataBaseFolderPath, "EXTENSION");

            var extensionFilePaths = Directory.EnumerateFiles(extensionDirectoryPath, "*.deps.json", SearchOption.AllDirectories)
                                              .Select(filePath => filePath.Replace(".deps.json", ".dll")).ToList();

            var idToDataReaderTypeMap = new Dictionary<string, Type>();
            var types = new List<Type>();

            // load assemblies
            foreach (var filePath in extensionFilePaths)
            {
                var loadContext = new ExtensionLoadContext(filePath);
                var assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(filePath));
                var assembly = loadContext.LoadFromAssemblyName(assemblyName);

                types.AddRange(this.ScanAssembly(assembly));
            }

#warning Improve this.
            // add additional data readers
            types.Add(typeof(AggregationDataReader));
            types.Add(typeof(InMemoryDataReader));
            types.Add(typeof(FilterDataReader));

            // get ID for each extension
            foreach (var type in types)
            {
                var attribute = type.GetFirstAttribute<ExtensionIdentificationAttribute>();
                idToDataReaderTypeMap[attribute.Id] = type;
            }

            // return root path to type map
            return dataReaderRegistrations.ToDictionary(registration => registration, registration =>
            {
                if (!idToDataReaderTypeMap.TryGetValue(registration.DataReaderId, out var type))
                    throw new Exception($"No data reader extension with ID '{registration.DataReaderId}' could be found.");

                return type;
            });
        }

        private List<Type> ScanAssembly(Assembly assembly)
        {
            return assembly.ExportedTypes.Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(DataReaderExtensionBase))).ToList();
        }

        private void CleanUpFilterProjects(List<ProjectInfo> filterProjects,
                                           List<ProjectMeta> projectMetas,
                                           UserManager<IdentityUser> userManager,
                                           CancellationToken cancellationToken)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var projectsToRemove = new List<ProjectInfo>();

            foreach (var project in filterProjects)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var projectMeta = projectMetas.First(projectMeta => projectMeta.Id == project.Id);
                var channelsToRemove = new List<ChannelInfo>();

                foreach (var channel in project.Channels)
                {
                    var datasetsToRemove = new List<DatasetInfo>();

                    foreach (var dataset in channel.Datasets)
                    {
                        var keep = false;

                        if (FilterDataReader.TryGetFilterCodeDefinition(dataset, out var codeDefinition))
                        {
                            // get user
                            if (!usersMap.TryGetValue(codeDefinition.Owner, out var user))
                            {
                                user = Utilities
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner, userManager)
                                    .Result;

                                usersMap[codeDefinition.Owner] = user;
                            }

                            keep = projectMeta.Id == FilterConstants.SharedProjectID || Utilities.IsProjectEditable(user, projectMeta);
                        }

                        if (!keep)
                            datasetsToRemove.Add(dataset);
                    }

                    foreach (var datasetToRemove in datasetsToRemove)
                    {
                        channel.Datasets.Remove(datasetToRemove);

                        if (!channel.Datasets.Any())
                            channelsToRemove.Add(channel);
                    }
                }

                foreach (var channelToRemove in channelsToRemove)
                {
                    project.Channels.Remove(channelToRemove);

                    if (!project.Channels.Any())
                        projectsToRemove.Add(project);
                }
            }

            foreach (var projectToRemove in projectsToRemove)
            {
                filterProjects.Remove(projectToRemove);
            }
        }

        #endregion
    }
}
