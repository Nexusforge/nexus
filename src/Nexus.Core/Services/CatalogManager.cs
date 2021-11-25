using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class CatalogManager : ICatalogManager
    {
        #region Types

        record CatalogPrototype(string CatalogId, BackendSource BackendSource, ClaimsPrincipal User);

        #endregion

        #region Fields

        public const string CommonCatalogsKey = "";

        private IDataControllerService _dataControllerService;
        private IDatabaseManager _databaseManager;
        private IUserManagerWrapper _userManagerWrapper;
        private SecurityOptions _securityOptions;
        private ILogger<CatalogManager> _logger;

        #endregion

        #region Constructors

        public CatalogManager(
            IDataControllerService dataControllerService, 
            IDatabaseManager databaseManager,
            IUserManagerWrapper userManagerWrapper,
            IOptions<SecurityOptions> securityOptions,
            ILogger<CatalogManager> logger)
        {
            _dataControllerService = dataControllerService;
            _databaseManager = databaseManager;
            _userManagerWrapper = userManagerWrapper;
            _securityOptions = securityOptions.Value;
            _logger = logger;
        }

        #endregion

        #region Methods

        public async Task<CatalogState> CreateCatalogStateAsync(CancellationToken cancellationToken)
        {
            /* create catalog cache */
            var state = new CatalogState(
                CatalogContainersMap: new CatalogContainersMap(),
                CatalogCache: new CatalogCache()
            );

            /* load builtin backend source */
            var builtinBackendSources = new BackendSource[]
            {
                new BackendSource(
                    Type: typeof(InMemory).FullName ?? throw new Exception("full name is null"),
                    ResourceLocator: new Uri("memory://localhost"),
                    Configuration: new Dictionary<string, string>()),
            };

            /* load all catalog identifiers */
            var path = "/";
            var catalogPrototypes = new List<CatalogPrototype>();

            /* => for the built-in backend sources */
            var rootUser = await _userManagerWrapper.GetClaimsPrincipalAsync(_securityOptions.RootUser);

            if (rootUser is not null)
            {
                /* for each backend source */
                foreach (var backendSource in builtinBackendSources)
                {
                    using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken, state.CatalogCache);
                    var catalogIds = await controller.GetCatalogIdsAsync(path, cancellationToken);

                    foreach (var catalogId in catalogIds)
                    {
                        catalogPrototypes.Add(new CatalogPrototype(catalogId, backendSource, rootUser));
                    }
                }
            }

            /* => for each user with existing config file */
            foreach (var jsonString in _databaseManager.EnumerateUserConfigs())
            {
                var userConfig = JsonSerializer.Deserialize<UserConfiguration>(jsonString) 
                    ?? throw new Exception("userConfig is null");

                var user = await _userManagerWrapper.GetClaimsPrincipalAsync(userConfig.Username);

                if (user is null)
                    continue;

                /* for each backend source */
                foreach (var backendSource in userConfig.BackendSources)
                {
                    using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken, state.CatalogCache);
                    var catalogIds = await controller.GetCatalogIdsAsync(path, cancellationToken);

                    foreach (var catalogId in catalogIds)
                    {
                        catalogPrototypes.Add(new CatalogPrototype(catalogId, backendSource, user));
                    }
                }
            }

            this.ProcessCatalogPrototypes(catalogPrototypes, state.CatalogContainersMap);

            /* done */
            _logger.LogInformation("Found {CatalogCount} catalogs.",
                state.CatalogContainersMap.SelectMany(entry => entry.Value).Count());

            return state;
        }

        public async Task AttachChildCatalogIdsAsync(
            CatalogContainer parent,
            CatalogContainersMap catalogContainersMap,
            CancellationToken cancellationToken)
        {
            using var controller = await _dataControllerService
                .GetDataSourceControllerAsync(parent.BackendSource, cancellationToken);

            var catalogIds = await controller
                .GetCatalogIdsAsync(parent.Id + "/", cancellationToken);

            var prototypes = catalogIds
                .Select(catalogId => new CatalogPrototype(catalogId, parent.BackendSource, parent.Owner));

            this.ProcessCatalogPrototypes(prototypes, catalogContainersMap);
        }

        public async Task<CatalogInfo> LoadCatalogInfoAsync(
            string catalogId, 
            BackendSource backendSource,
            ResourceCatalog? catalogOverrides,
            CancellationToken cancellationToken)
        {
            var catalogBegin = default(DateTime);
            var catalogEnd = default(DateTime);

            using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
            var catalog = await controller.GetCatalogAsync(catalogId, cancellationToken);

            // get begin and end of project
            var timeRangeResult = await controller.GetTimeRangeAsync(catalog.Id, cancellationToken);

            // merge time range
            if (catalogBegin == DateTime.MinValue)
                catalogBegin = timeRangeResult.Begin;

            else
                catalogBegin = new DateTime(Math.Min(catalogBegin.Ticks, timeRangeResult.Begin.Ticks));

            if (catalogEnd == DateTime.MinValue)
                catalogEnd = timeRangeResult.End;

            else
                catalogEnd = new DateTime(Math.Max(catalogEnd.Ticks, timeRangeResult.End.Ticks));

            // merge catalog
            if (catalogOverrides is not null)
                catalog = catalog.Merge(catalogOverrides, MergeMode.NewWins);

            return new CatalogInfo(catalogBegin, catalogEnd, catalog);
        }

        private void ProcessCatalogPrototypes(
            IEnumerable<CatalogPrototype> catalogPrototypes,
            CatalogContainersMap catalogContainersMap)
        {
            /* clean up */
            catalogPrototypes = this.EnsureNoHierarchy(catalogPrototypes);

            /* get common catalog */
            if (!catalogContainersMap.TryGetValue(CatalogManager.CommonCatalogsKey, out var commonCatalogContainers))
            {
                commonCatalogContainers = new List<CatalogContainer>();
                catalogContainersMap[CatalogManager.CommonCatalogsKey] = commonCatalogContainers;
            }

            /* for each catalog prototype */
            foreach (var catalogPrototype in catalogPrototypes)
            {
                /* get user specific catalog */
                var identity = catalogPrototype.User.Identity ?? throw new Exception("identity is null");
                var username = identity.Name ?? throw new Exception("name is null");

                if (!catalogContainersMap.TryGetValue(username, out var userCatalogContainers))
                {
                    userCatalogContainers = new List<CatalogContainer>();
                    catalogContainersMap[username] = userCatalogContainers;
                }

                /* distribute to common and user specific catalog array, respectively */
                var catalogContainers = AuthorizationUtilities.IsCatalogEditable(catalogPrototype.User, catalogPrototype.CatalogId)
                    ? commonCatalogContainers
                    : userCatalogContainers;

                /* create catalog metadata */
                CatalogMetadata catalogMetadata;

                if (_databaseManager.TryReadCatalogMetadata(catalogPrototype.CatalogId, out var jsonString2))
                    catalogMetadata = JsonSerializer.Deserialize<CatalogMetadata>(jsonString2) ?? throw new Exception("catalogMetadata is null");

                else
                    catalogMetadata = new CatalogMetadata();

                /* create catalog container */
                var catalogContainer = new CatalogContainer(
                    catalogPrototype.CatalogId,
                    catalogPrototype.User,
                    catalogPrototype.BackendSource,
                    catalogMetadata,
                    this);

                /* add to array */
                catalogContainers.Add(catalogContainer);
            }
        }

        private List<CatalogPrototype> EnsureNoHierarchy(
            IEnumerable<CatalogPrototype> catalogPrototypes)
        {
            // Background:
            //
            // Nexus allows catalogs to have child catalogs like folders in a file system. To simplify things,
            // it is required that a catalog that comes from a certain backend source can only have child
            // catalogs of the very same backend source.
            // 
            // In general, child catalogs will be loaded lazily. Therefore, for any catalog of the provided array that
            // appears to be a child catalog, it can be assumed it comes from a backend source other than the one
            // from the parent catalog. Depending on the user's rights, this method decides which one will survive.
            // 
            //
            // Example:
            //
            // The following combination of catalogs is allowed:
            // Backend source 1: /a + /a/a + /a/b
            // Backend source 2: /b/c
            //
            // The following combination of catalogs is forbidden:
            // Backend source 1: /a + /a/a + /a/b
            // Backend source 2: /a/c

            var catalogPrototypesToKeep = new List<CatalogPrototype>();

            foreach (var catalogPrototype in catalogPrototypes)
            {
                var referenceIndex = catalogPrototypesToKeep.FindIndex(
                    current =>
                        current.CatalogId.StartsWith(catalogPrototype.CatalogId) ||
                        catalogPrototype.CatalogId.StartsWith(current.CatalogId));

                /* nothing found */
                if (referenceIndex < 0)
                {
                    catalogPrototypesToKeep.Add(catalogPrototype);
                }

                /* reference found */
                else
                {
                    var user = catalogPrototype.User;
                    var otherUser = catalogPrototypesToKeep[referenceIndex].User;

                    /* other user is no admin, but current user is */
                    if (!otherUser.HasClaim(Claims.IS_ADMIN, "true") && user.HasClaim(Claims.IS_ADMIN, "true"))
                        catalogPrototypesToKeep[referenceIndex] = catalogPrototype;
                }
            }

            return catalogPrototypesToKeep;
        }

        #endregion
    }
}
