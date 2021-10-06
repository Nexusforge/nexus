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

        public event EventHandler<CatalogCollection> CatalogsUpdated;

        #endregion

        #region Fields

        private bool _isInitialized;
        private ILogger<DatabaseManager> _logger;
        private ILoggerFactory _loggerFactory;
        private IServiceProvider _serviceProvider;

        #endregion

        #region Constructors

        public CatalogManager(IServiceProvider serviceProvider, ILogger<DatabaseManager> logger, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loggerFactory = loggerFactory;
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
            var catalogCollection = new CatalogCollection();

            // create new empty catalogs map
            var backendSourceToCatalogsMap = new Dictionary<BackendSource, ResourceCatalog[]>();

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

            var dataSources = await Task.WhenAll(tasks);

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
                        return JsonSerializer.Deserialize<ResourceCatalog>(jsonString);
                    }
                    else
                    {
                        return new Catalog(catalogId);
                    }
                })
                .ToList();

            // ensure that the filter data reader plugin does not create catalogs and resources without permission
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
                    var container = catalogCollection.CatalogContainers.FirstOrDefault(container => container.Id == catalog.Id);

                    if (container == null)
                    {
                        var catalogMeta = catalogMetas.First(catalogMeta => catalogMeta.Id == catalog.Id);

                        container = new CatalogContainer(catalog.Id);
                        container.CatalogSettings = catalogMeta;
                        catalogCollection.CatalogContainers.Add(container);
                    }

                    container.Catalog.Merge(catalog, MergeMode.ExclusiveOr);
                }
            }

            // the purpose of this block is to initalize empty properties,
            // add missing resources and clean up empty resources
            foreach (var catalogContainer in catalogCollection.CatalogContainers)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // remove all resources where no native representations are available
                // because only these provide metadata like name and group
                var resources = catalogContainer.Catalog.Resources;

                resources
                    .Where(resource => string.IsNullOrWhiteSpace(resource.Id))
                    .ToList()
                    .ForEach(resource => resources.Remove(resource));

                // save catalog meta to disk
                this.SaveCatalogMeta(catalogContainer.CatalogSettings);
            }

            this.State = new CatalogManagerState()
            {
                AggregationBackendSource = backendSource,
                Catalogs = catalogCollection,
                BackendSourceToCatalogsMap = backendSourceToCatalogsMap,
                BackendSourceToDataReaderTypeMap = backendSourceToDataReaderTypeMap
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

            _isInitialized = true;
        }

        private void CleanUpFilterCatalogs(List<ResourceCatalog> filterCatalogs,
                                           List<ResourceCatalog> catalogMetas,
                                           UserManager<IdentityUser> userManager,
                                           CancellationToken cancellationToken)
        {
            var usersMap = new Dictionary<string, ClaimsPrincipal>();
            var catalogsToRemove = new List<ResourceCatalog>();

            foreach (var catalog in filterCatalogs)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var catalogMeta = catalogMetas.First(catalogMeta => catalogMeta.Id == catalog.Id);
                var resourcesToRemove = new List<Resource>();

                foreach (var resource in catalog.Resources)
                {
                    var representationsToRemove = new List<Representation>();

                    foreach (var representation in resource.Representations)
                    {
                        var keep = false;

                        if (FilterDataSource.TryGetFilterCodeDefinition(representation, out var codeDefinition))
                        {
                            // get user
                            if (!usersMap.TryGetValue(codeDefinition.Owner, out var user))
                            {
                                user = NexusUtilities
                                    .GetClaimsPrincipalAsync(codeDefinition.Owner, userManager)
                                    .Result;

                                usersMap[codeDefinition.Owner] = user;
                            }

                            keep = catalogMeta.Id == FilterConstants.SharedCatalogID || NexusUtilities.IsCatalogEditable(user, catalogMeta);
                        }

                        if (!keep)
                            representationsToRemove.Add(representation);
                    }

                    foreach (var representationToRemove in representationsToRemove)
                    {
                        resource.Representations.Remove(representationToRemove);

                        if (!resource.Representations.Any())
                            resourcesToRemove.Add(resource);
                    }
                }

                foreach (var resourceToRemove in resourcesToRemove)
                {
                    catalog.Resources.Remove(resourceToRemove);

                    if (!catalog.Resources.Any())
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
