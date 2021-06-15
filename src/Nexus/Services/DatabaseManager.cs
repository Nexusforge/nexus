using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Nexus.DataModel;

namespace Nexus.Services
{
    /* 
     * Algorithm:
     * *******************************************************************************
     * 01. load database.json (this.Database)
     * 02. load and instantiate data reader extensions (_rootPathToDataReaderMap)
     * 03. call Update() method
     * 04.   for each data reader in _rootPathToDataReaderMap
     * 05.       get catalog names
     * 06.           for each catalog name
     * 07.               find catalog container in current database or create new one
     * 08.               get an up-to-date catalog instance from the data reader
     * 09.               merge both catalogs
     * 10. save updated database
     * *******************************************************************************
     */

    public class DatabaseManager
    {
        #region Records

        public record DatabaseManagerState
        {
            public DataSourceRegistration AggregationRegistration { get; init; }
            public NexusDatabase Database { get; init; }
            public Dictionary<DataSourceRegistration, Type> RegistrationToDataReaderTypeMap { get; init; }
            public Dictionary<DataSourceRegistration, List<Catalog>> RegistrationToCatalogsMap { get; init; }
        }

        #endregion

        #region Events

        public event EventHandler<NexusDatabase> DatabaseUpdated;

        #endregion

        #region Fields

        private NexusOptions _options;

        private bool _isInitialized;
        private ILogger<DatabaseManager> _logger;
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
            // 1) registrationToCatalogsMap, registrationToDataReaderTypeMap and database are instantiated in this method,
            // combined into a new DatabaseManagerState and then set in an atomic operation to the State propery.
            // 
            // 2) Within this method, the registrationToCatalogsMap cache gets filled
            //
            // 3) It may happen that during this process, which might take a while, an external caller calls 
            // GetDataReader. To divide both processes (external call vs this method),the State property is introduced, 
            // so external calls use old maps and this method uses the new instances.

            FilterDataSource.ClearCache();
            var database = new NexusDatabase();

            // create new empty catalogs map
            var registrationToCatalogsMap = new Dictionary<DataSourceRegistration, List<Catalog>>();

            // load data readers
            var registrationToDataReaderTypeMap = this.LoadDataReaders(this.Config.DataSourceRegistrations);

            // register aggregation data reader
            var registration = new DataSourceRegistration()
            {
                DataSourceId = "Nexus.Aggregation",
                ResourceLocator = new Uri(
                    !string.IsNullOrWhiteSpace(this.Config.AggregationDataReaderRootPath)
                        ? this.Config.AggregationDataReaderRootPath
                        : _options.DataBaseFolderPath,
                    UriKind.RelativeOrAbsolute
                )
            };

            registrationToDataReaderTypeMap[registration] = typeof(AggregationDataSource);

            // get catalogs
            var dataSources = registrationToDataReaderTypeMap
                                .Select(entry => this.InstantiateDataSource(entry.Key, entry.Value, registrationToCatalogsMap))
                                .ToList();

            foreach (var dataSource in dataSources)
            {
                try
                {
                    dataSource.Dispose();
                }
                catch { }
            }

            // get catalog meta data
            var catalogMetas = registrationToCatalogsMap
                .SelectMany(entry => entry.Value)
                .Select(catalog => catalog.Id)
                .Distinct()
                .Select(catalogId =>
                {
                    var filePath = this.GetCatalogMetaPath(catalogId);

                    if (File.Exists(filePath))
                    {
                        var jsonString = File.ReadAllText(filePath);
                        return JsonSerializer.Deserialize<CatalogMeta>(jsonString);
                    }
                    else
                    {
                        return new CatalogMeta(catalogId);
                    }
                })
                .ToList();

            // ensure that the filter data reader plugin does not create catalogs and channels without permission
            var filterCatalogs = registrationToCatalogsMap
                .Where(entry => entry.Key.DataSourceId == FilterDataSource.Id)
                .SelectMany(entry => entry.Value)
                .ToList();

            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                this.CleanUpFilterCatalogs(filterCatalogs, catalogMetas, userManager, cancellationToken);
            }

            // merge all catalogs
            foreach (var entry in registrationToCatalogsMap)
            {
                foreach (var catalog in entry.Value)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    // find catalog container or create a new one
                    var container = database.CatalogContainers.FirstOrDefault(container => container.Id == catalog.Id);

                    if (container == null)
                    {
                        var catalogMeta = catalogMetas.First(catalogMeta => catalogMeta.Id == catalog.Id);

                        container = new CatalogContainer(catalog.Id);
                        container.CatalogMeta = catalogMeta;
                        database.CatalogContainers.Add(container);
                    }

                    container.Catalog.Merge(catalog, ChannelMergeMode.OverwriteMissing);
                }
            }

            // the purpose of this block is to initalize empty properties,
            // add missing channels and clean up empty channels
            foreach (var catalogContainer in database.CatalogContainers)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // remove all channels where no native datasets are available
                // because only these provide metadata like name and group
                var channels = catalogContainer.Catalog.Channels;

                channels
                    .Where(channel => string.IsNullOrWhiteSpace(channel.Name))
                    .ToList()
                    .ForEach(channel => channels.Remove(channel));

                // initalize catalog meta
                catalogContainer.CatalogMeta.Initialize(catalogContainer.Catalog);

                // save catalog meta to disk
                this.SaveCatalogMeta(catalogContainer.CatalogMeta);
            }

            this.State = new DatabaseManagerState()
            {
                AggregationRegistration = registration,
                Database = database,
                RegistrationToCatalogsMap = registrationToCatalogsMap,
                RegistrationToDataReaderTypeMap = registrationToDataReaderTypeMap
            };

            this.DatabaseUpdated?.Invoke(this, database);
            _logger.LogInformation("Database loaded.");
        }

        public List<DataSourceController> GetDataReaders(ClaimsPrincipal user, string catalogId)
        {
            var state = this.State;

            return state.RegistrationToCatalogsMap
                // where the catalog list contains the catalog ID
                .Where(entry => entry.Value.Any(catalog => catalog.Id == catalogId))
                // select the registration and get a brand new data reader from it
                .Select(entry => this.GetDataSourceController(user, entry.Key, state))
                // to list
                .ToList();
        }

        public DataSourceController GetDataSourceController(
                ClaimsPrincipal user,
                DataSourceRegistration registration,
                DatabaseManagerState state = null)
        {
            if (state == null)
                state = this.State;

            if (!state.RegistrationToDataReaderTypeMap.TryGetValue(registration, out var dataSourceType))
                throw new KeyNotFoundException("The requested data source could not be found.");

            var dataSource = this.InstantiateDataSource(registration, dataSourceType, state.RegistrationToCatalogsMap);

            // special case checks
            if (dataSourceType == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)dataSource;

                filterDataSource.Database = this.Database;

                filterDataSource.IsCatalogAccessible = 
                    catalogId => Utilities.IsCatalogAccessible(user, catalogId, this.Database);

                filterDataSource.GetDataReader = registration => this.GetDataSourceController(user, registration);
            }

            return dataSource;
        }

        public void SaveCatalogMeta(CatalogMeta catalogMeta)
        {
            var filePath = this.GetCatalogMetaPath(catalogMeta.Id);
            var jsonString = JsonSerializer.Serialize(catalogMeta, new JsonSerializerOptions() { WriteIndented = true });
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
                    var inMemoryRegistration = new DataSourceRegistration()
                    {
                        DataSourceId = "Nexus.InMemory",
                        ResourceLocator = null
                    };

                    if (!this.Config.DataSourceRegistrations.Contains(inMemoryRegistration))
                        this.Config.DataSourceRegistrations.Add(inMemoryRegistration);

                    var filterRegistration = new DataSourceRegistration()
                    {
                        DataSourceId = "Nexus.Filters",
                        ResourceLocator = new Uri(_options.DataBaseFolderPath, UriKind.RelativeOrAbsolute)
                    };

                    if (!this.Config.DataSourceRegistrations.Contains(filterRegistration))
                        this.Config.DataSourceRegistrations.Add(filterRegistration);

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

        private async Task<DataSourceController> 
            InstantiateDataSourceAsync(DataSourceRegistration registration, Type type, Dictionary<DataSourceRegistration, List<Catalog>> registrationToCatalogsMap)
        {
            _logger.LogInformation($"Instantiating {registration.DataSourceId} for URI {registration.ResourceLocator} ...");

            var logger = _loggerFactory.CreateLogger($"{registration.DataSourceId} - {registration.ResourceLocator}");
            var dataSource = (IDataSource)Activator.CreateInstance(type);

            // set parameters
            dataSource.ResourceLocator = registration.ResourceLocator;
            dataSource.Parameters = registration.Parameters;
            dataSource.Logger = logger;

            await dataSource.OnParametersSetAsync();

            // special case checks
            if (type == typeof(AggregationDataSource))
            {
                var fileAccessManger = _serviceProvider.GetRequiredService<IFileAccessManager>();
                ((AggregationDataSource)dataSource).FileAccessManager = fileAccessManger;
            }

            // create controller 
            var controller = new DataSourceController(dataSource);

            if (registrationToCatalogsMap.TryGetValue(registration, out var value))
            {
                controller.InitializeCatalogs(value);
            }
            else
            {
                controller.InitializeCatalogs();

                registrationToCatalogsMap[registration] = controller
                    .Catalogs
                    .Where(current => NexusUtilities.CheckCatalogNamingConvention(current.Id, out var _))
                    .ToList();
            }

            return controller;
        }

        private string GetCatalogMetaPath(string catalogName)
        {
            return Path.Combine(_options.DataBaseFolderPath, "META", $"{catalogName.TrimStart('/').Replace('/', '_')}.json");
        }

        private Dictionary<DataSourceRegistration, Type> LoadDataReaders(List<DataSourceRegistration> DataSourceRegistrations)
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
            types.Add(typeof(AggregationDataSource));
            types.Add(typeof(InMemoryDataSource));
            types.Add(typeof(FilterDataSource));

            // get ID for each extension
            foreach (var type in types)
            {
                var attribute = type.GetFirstAttribute<ExtensionIdentificationAttribute>();
                idToDataReaderTypeMap[attribute.Id] = type;
            }

            // return root path to type map
            return DataSourceRegistrations.ToDictionary(registration => registration, registration =>
            {
                if (!idToDataReaderTypeMap.TryGetValue(registration.DataSourceId, out var type))
                    throw new Exception($"No data reader extension with ID '{registration.DataSourceId}' could be found.");

                return type;
            });
        }

        private List<Type> ScanAssembly(Assembly assembly)
        {
            return assembly.ExportedTypes.Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(DataSourceController))).ToList();
        }

        private void CleanUpFilterCatalogs(List<Catalog> filterCatalogs,
                                           List<CatalogMeta> catalogMetas,
                                           UserManager<IdentityUser> userManager,
                                           CancellationToken cancellationToken)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var catalogsToRemove = new List<Catalog>();

            foreach (var catalog in filterCatalogs)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var catalogMeta = catalogMetas.First(catalogMeta => catalogMeta.Id == catalog.Id);
                var channelsToRemove = new List<Channel>();

                foreach (var channel in catalog.Channels)
                {
                    var datasetsToRemove = new List<Dataset>();

                    foreach (var dataset in channel.Datasets)
                    {
                        var keep = false;

                        if (FilterDataSource.TryGetFilterCodeDefinition(dataset, out var codeDefinition))
                        {
                            // get user
                            if (!usersMap.TryGetValue(codeDefinition.Owner, out var user))
                            {
                                user = Utilities
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner, userManager)
                                    .Result;

                                usersMap[codeDefinition.Owner] = user;
                            }

                            keep = catalogMeta.Id == FilterConstants.SharedCatalogID || Utilities.IsCatalogEditable(user, catalogMeta);
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
                    catalog.Channels.Remove(channelToRemove);

                    if (!catalog.Channels.Any())
                        catalogsToRemove.Add(catalog);
                }
            }

            foreach (var catalogToRemove in catalogsToRemove)
            {
                filterCatalogs.Remove(catalogToRemove);
            }
        }

        #endregion
    }
}
