using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Filters;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
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

    internal class CatalogManager : ICatalogManager
    {
        #region Events

        public event EventHandler<CatalogContainerCollection> CatalogsUpdated;

        #endregion

        #region Fields

        private bool _isInitialized;
        private IDataControllerService _dataControllerService;
        private IDatabaseManager _databaseManager;
        private IServiceProvider _serviceProvider;
        private ILogger<CatalogManager> _logger;
        private PathsOptions _options;
        private string _dataPath;

        #endregion

        #region Constructors

        public CatalogManager(
            IDataControllerService dataControllerService, 
            IDatabaseManager databaseManager,
            IServiceProvider serviceProvider,
            ILogger<CatalogManager> logger,
            IOptions<PathsOptions> options)
        {
            _dataControllerService = dataControllerService;
            _databaseManager = databaseManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;

            _dataPath = Path.Combine(_options.Cache, "..", "data");
        }

        #endregion

        #region Properties

        public CatalogManagerState State { get; private set; }

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

            // load data sources and get catalogs
            var backendSourceToCatalogsMap = (await Task.WhenAll(
                this.Config.BackendSources.Select(async backendSource =>
                    {
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogs = await controller.GetCatalogsAsync(cancellationToken);

                        // ensure that the filter data reader plugin does not create catalogs and resources without permission
                        if (backendSource.Type == FilterDataSource.Id)
                        {
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                                catalogs = this.CleanUpFilterCatalogs(catalogs, userManager);
                            }
                        }

                        return new KeyValuePair<BackendSource, ResourceCatalog[]>(backendSource, catalogs);
                    })))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            // instantiate aggregation data reader
            var backendSource = new BackendSource()
            {
                Type = "Nexus.Aggregation",
                ResourceLocator = new Uri(
                    !string.IsNullOrWhiteSpace(this.Config.AggregationDataReaderRootPath)
                        ? this.Config.AggregationDataReaderRootPath
                        : _dataPath,
                    UriKind.RelativeOrAbsolute
                )
            };

            using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
            var catalogs = await controller.GetCatalogsAsync(cancellationToken);
            backendSourceToCatalogsMap[backendSource] = catalogs;

            // merge all catalogs
            var idToCatalogContainerMap = new Dictionary<string, CatalogContainer>();

            foreach (var entry in backendSourceToCatalogsMap)
            {
                foreach (var catalog in entry.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // find catalog container or create a new one
                    if (!idToCatalogContainerMap.ContainsKey(catalog.Id))
                    {
                        var metadata = default(CatalogMetadata);

                        if (_databaseManager.TryReadCatalogMetadata(catalog.Id, out var stream))
                        {
                            var jsonString = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
                            metadata = JsonSerializerHelper.Deserialize<CatalogMetadata>(jsonString);
                        }
                        else
                        {
                            metadata = new CatalogMetadata();
                        }

                        var container = new CatalogContainer(catalog, metadata);

                        idToCatalogContainerMap[catalog.Id] = container;
                    }


                    container.Catalog.Merge(catalog, MergeMode.ExclusiveOr);
                }
            }

            this.State = new CatalogManagerState()
            {
                AggregationBackendSource = backendSource,
                Catalogs = catalogCollection,
                BackendSourceToCatalogsMap = backendSourceToCatalogsMap,
            };

            this.CatalogsUpdated?.Invoke(this, catalogCollection);
            _logger.LogInformation("Database loaded.");
        }

        private void Initialize()
        {
            try
            {
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
                var inmemoryBackendSource = new BackendSource()
                {
                    Type = "Nexus.Builtin.Inmemory",
                    ResourceLocator = new Uri("memory://localhost")
                };

                if (!this.Config.BackendSources.Contains(inmemoryBackendSource))
                    this.Config.BackendSources.Add(inmemoryBackendSource);

                var filterBackendSource = new BackendSource()
                {
                    Type = "Nexus.Builtin.Filters",
                    ResourceLocator = new Uri(_dataPath, UriKind.RelativeOrAbsolute)
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

            _isInitialized = true;
        }

        private ResourceCatalog[] CleanUpFilterCatalogs(
            ResourceCatalog[] filterCatalogs,
            UserManager<IdentityUser> userManager)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var filteredCatalogs = new List<ResourceCatalog>();

            foreach (var catalog in filterCatalogs)
            {
                var resources = new List<Resource>();

                foreach (var resource in catalog.Resources)
                {
                    var representations = new List<Representation>();

                    foreach (var representation in resource.Representations)
                    {
                        if (FilterDataSource.TryGetFilterCodeDefinition(resource.Id, representation.BackendSource, out var codeDefinition))
                        {
                            // get user
                            if (!usersMap.TryGetValue(codeDefinition.Owner, out var user))
                            {
                                user = NexusUtilities
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner, userManager)
                                    .Result;

                                usersMap[codeDefinition.Owner] = user;
                            }

                            var keep = catalog.Id == FilterConstants.SharedCatalogID || NexusUtilities.IsCatalogEditable(user, catalog.Id);

                            if (keep)
                                representations.Add(representation);
                        }
                    }

                    if (representations.Any())
                        resources.Add(resource with { Representations = representations });
                }

                if (resources.Any())
                    filteredCatalogs.Add(catalog with { Resources = resources });
            }

            return filteredCatalogs.ToArray();
        }

        #endregion
    }
}
