using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
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
     * 05.       get catalog names
     * 06.           for each catalog name
     * 07.               find catalog container in current database or create new one
     * 08.               get an up-to-date catalog instance from the data reader
     * 09.               merge both catalogs
     * 10. save updated database
     * *******************************************************************************
     */

    public class DatabaseManager : IDatabaseManager
    {
        #region Records

        public record DatabaseManagerState
        {
            public BackendSource AggregationBackendSource { get; init; }
            public NexusDatabase Database { get; init; }
            public Dictionary<BackendSource, Type> BackendSourceToDataReaderTypeMap { get; init; }
            public Dictionary<BackendSource, List<Catalog>> BackendSourceToCatalogsMap { get; init; }
        }

        #endregion

        #region Events

        public event EventHandler<NexusDatabase> DatabaseUpdated;

        #endregion

        #region Fields

        private PathsOptions _pathsOptions;

        private bool _isInitialized;
        private ILogger<DatabaseManager> _logger;
        private ILoggerFactory _loggerFactory;
        private IServiceProvider _serviceProvider;

        #endregion

        #region Constructors

        public DatabaseManager(IServiceProvider serviceProvider, ILogger<DatabaseManager> logger, ILoggerFactory loggerFactory, IOptions<PathsOptions> pathsOptions)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _pathsOptions = pathsOptions.Value;
        }

        #endregion

        #region Properties

        public NexusDatabase Database => this.State?.Database;

        public DatabaseManagerState State { get; private set; }

        private NexusDatabaseConfig Config { get; set; }

        #endregion

        #region Methods

        public async Task UpdateAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized)
                this.Initialize();

            // Concept:
            //
            // 1) backendSourceToCatalogsMap, backendSourceToDataSourceTypeMap and database are instantiated in this method,
            // combined into a new DatabaseManagerState and then set in an atomic operation to the State propery.
            // 
            // 2) Within this method, the backendSourceToCatalogsMap cache gets filled
            //
            // 3) It may happen that during this process, which might take a while, an external caller calls 
            // GetDataReader. To divide both processes (external call vs this method),the State property is introduced, 
            // so external calls use old maps and this method uses the new instances.

            FilterDataSource.ClearCache();
            var database = new NexusDatabase();

            // create new empty catalogs map
            var backendSourceToCatalogsMap = new Dictionary<BackendSource, List<Catalog>>();

            // load data readers
            var backendSourceToDataReaderTypeMap = this.LoadDataReaders(this.Config.BackendSources);

            // register aggregation data reader
            var backendSource = new BackendSource()
            {
                Type = "Nexus.Aggregation",
                ResourceLocator = new Uri(
                    !string.IsNullOrWhiteSpace(this.Config.AggregationDataReaderRootPath)
                        ? this.Config.AggregationDataReaderRootPath
                        : _pathsOptions.Data,
                    UriKind.RelativeOrAbsolute
                )
            };

            backendSourceToDataReaderTypeMap[backendSource] = typeof(AggregationDataSource);

            // get catalogs
            var tasks = backendSourceToDataReaderTypeMap
                                .Select(entry => this.InstantiateDataSourceAsync(entry.Key, entry.Value, backendSourceToCatalogsMap, cancellationToken))
                                .ToList();

            await Task.WhenAll(tasks);
            var dataSources = tasks.Select(task => task.Result);

            foreach (var dataSource in dataSources)
            {
                try
                {
                    dataSource.Dispose();
                }
                catch { }
            }

            // get catalog meta data
            var catalogMetas = backendSourceToCatalogsMap
                .SelectMany(entry => entry.Value)
                .Select(catalog => catalog.Id)
                .Distinct()
                .Select(catalogId =>
                {
                    var filePath = this.GetCatalogMetaPath(catalogId);

                    if (File.Exists(filePath))
                    {
                        var jsonString = File.ReadAllText(filePath);
                        return JsonSerializer.Deserialize<Catalog>(jsonString);
                    }
                    else
                    {
                        return new Catalog(catalogId);
                    }
                })
                .ToList();

            // ensure that the filter data reader plugin does not create catalogs and channels without permission
            var filterCatalogs = backendSourceToCatalogsMap
                .Where(entry => entry.Key.Type == FilterDataSource.Id)
                .SelectMany(entry => entry.Value)
                .ToList();

            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                this.CleanUpFilterCatalogs(filterCatalogs, catalogMetas, userManager, cancellationToken);
            }

            // merge all catalogs
            foreach (var entry in backendSourceToCatalogsMap)
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
                        container.CatalogSettings = catalogMeta;
                        database.CatalogContainers.Add(container);
                    }

                    container.Catalog.Merge(catalog, MergeMode.ExclusiveOr);
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

                // save catalog meta to disk
                this.SaveCatalogMeta(catalogContainer.CatalogSettings);
            }

            this.State = new DatabaseManagerState()
            {
                AggregationBackendSource = backendSource,
                Database = database,
                BackendSourceToCatalogsMap = backendSourceToCatalogsMap,
                BackendSourceToDataReaderTypeMap = backendSourceToDataReaderTypeMap
            };

            this.DatabaseUpdated?.Invoke(this, database);
            _logger.LogInformation("Database loaded.");
        }

        public async Task<List<DataSourceController>> GetDataSourcesAsync(ClaimsPrincipal user, string catalogId, CancellationToken cancellationToken)
        {
            var state = this.State;

            var tasks = state.BackendSourceToCatalogsMap
                // where the catalog list contains the catalog ID
                .Where(entry => entry.Value.Any(catalog => catalog.Id == catalogId))
                // select the backend source and get a brand new data source from it
                .Select(entry => this.GetDataSourceControllerAsync(user, entry.Key, cancellationToken, state));

            await Task.WhenAll(tasks);

            return tasks
                .Select(task => task.Result)
                .ToList();
        }

        public async Task<DataSourceController> GetDataSourceControllerAsync(
                ClaimsPrincipal user,
                BackendSource backendSource,
                CancellationToken cancellationToken,
                DatabaseManagerState state = null)
        {
            if (state == null)
                state = this.State;

            if (!state.BackendSourceToDataReaderTypeMap.TryGetValue(backendSource, out var dataSourceType))
                throw new KeyNotFoundException("The requested data source could not be found.");

            var controller = await this.InstantiateDataSourceAsync(backendSource, dataSourceType, state.BackendSourceToCatalogsMap, cancellationToken);

            // special case checks
            if (dataSourceType == typeof(FilterDataSource))
            {
                var filterDataSource = (FilterDataSource)controller.DataSource;

                filterDataSource.Database = this.Database;

                filterDataSource.IsCatalogAccessible =
                    catalogId => Utilities.IsCatalogAccessible(user, catalogId, this.Database);

                filterDataSource.GetDataSourceAsync =
                    backendSource => this.GetDataSourceControllerAsync(user, backendSource, cancellationToken);
            }

            return controller;
        }

        public void SaveCatalogMeta(Catalog catalogMeta)
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
            var dbFolderPath = _pathsOptions.Data;

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
                    var inMemoryBackendSource = new BackendSource()
                    {
                        Type = "Nexus.InMemory",
                        ResourceLocator = new Uri("memory://localhost")
                    };

                    if (!this.Config.BackendSources.Contains(inMemoryBackendSource))
                        this.Config.BackendSources.Add(inMemoryBackendSource);

                    var filterBackendSource = new BackendSource()
                    {
                        Type = "Nexus.Filters",
                        ResourceLocator = new Uri(_pathsOptions.Data, UriKind.RelativeOrAbsolute)
                    };

                    if (!this.Config.BackendSources.Contains(filterBackendSource))
                        this.Config.BackendSources.Add(filterBackendSource);

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
            InstantiateDataSourceAsync(BackendSource backendSource, Type type, Dictionary<BackendSource, List<Catalog>> backendSourceToCatalogsMap, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Instantiating {backendSource.Type} for URI {backendSource.ResourceLocator} ...");

            var logger = _loggerFactory.CreateLogger($"{backendSource.Type} - {backendSource.ResourceLocator}");
            var dataSource = (IDataSource)Activator.CreateInstance(type);

            // special case checks
            if (type == typeof(AggregationDataSource))
            {
                var fileAccessManger = _serviceProvider.GetRequiredService<IFileAccessManager>();
                ((AggregationDataSource)dataSource).FileAccessManager = fileAccessManger;
            }

            // create controller 
            var controller = new DataSourceController(dataSource, backendSource, logger);

            // initialize
            _ = backendSourceToCatalogsMap.TryGetValue(backendSource, out var catalogs);

            var context = new DataSourceContext()
            {
                ResourceLocator = backendSource.ResourceLocator,
                Configuration = backendSource.Configuration,
                Logger = logger,
                Catalogs = catalogs
            };

            await controller.InitializeAsync(context, cancellationToken);
            backendSourceToCatalogsMap[backendSource] = controller.Catalogs;

            return controller;
        }

        private string GetCatalogMetaPath(string catalogName)
        {
            return Path.Combine(_pathsOptions.Data, "META", $"{catalogName.TrimStart('/').Replace('/', '_')}.json");
        }

        private Dictionary<BackendSource, Type> LoadDataReaders(List<BackendSource> backendSources)
        {
            var extensionFilePaths = Directory.EnumerateFiles(_pathsOptions.Extensions, "*.deps.json", SearchOption.AllDirectories)
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
            return backendSources.ToDictionary(backendSource => backendSource, backendSource =>
            {
                if (!idToDataReaderTypeMap.TryGetValue(backendSource.Type, out var type))
                    throw new Exception($"No data reader extension with ID '{backendSource.Type}' could be found.");

                return type;
            });
        }

        private List<Type> ScanAssembly(Assembly assembly)
        {
            return assembly.ExportedTypes.Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(DataSourceController))).ToList();
        }

        private void CleanUpFilterCatalogs(List<Catalog> filterCatalogs,
                                           List<Catalog> catalogMetas,
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
