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
            var catalogCache = new CatalogCache();
            var catalogContainersMap = new Dictionary<string, List<CatalogContainer>>();

            var state = new CatalogState(
                CatalogContainersMap: catalogContainersMap,
                CatalogCache: catalogCache
            );

            /* load builtin backend source */
            var builtinBackendSources = new BackendSource[]
            {
                new BackendSource(
                    Type: typeof(InMemory).FullName ?? throw new Exception("full name is null"),
                    ResourceLocator: new Uri("memory://localhost"),
                    Configuration: new Dictionary<string, string>()),
            };

            foreach (var builtinBackendSource in builtinBackendSources)
            {
                var user = await _userManagerWrapper.GetClaimsPrincipalAsync(_securityOptions.RootUser);

                if (user is not null)
                    await this.LoadCatalogIdsAsync("/", builtinBackendSource, user, state, cancellationToken);
            }

            /* for each user with existing config file */
            foreach (var jsonString in _databaseManager.EnumerateUserConfigs())
            {
                var userConfig = JsonSerializer.Deserialize<UserConfiguration>(jsonString) 
                    ?? throw new Exception("userConfig is null");

                var user = await _userManagerWrapper.GetClaimsPrincipalAsync(userConfig.Username);

                if (user is null)
                    continue;

                /* for each backend source */
                foreach (var backendSource in userConfig.BackendSources.Where(backendSource => backendSource.IsEnabled))
                {
                    await this.LoadCatalogIdsAsync("/", backendSource, user, state, cancellationToken);
                }
            }

            _logger.LogInformation("Found {CatalogCount} catalogs.",
                catalogContainersMap.SelectMany(entry => entry.Value).Count());

            return state;
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

        public async Task LoadCatalogIdsAsync(
            string relativeTo,
            BackendSource backendSource,
            ClaimsPrincipal user,
            CatalogState state,
            CancellationToken cancellationToken)
        {
            try
            {
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken, state.CatalogCache);
                var catalogIds = await controller.GetCatalogIdsAsync(relativeTo, cancellationToken);

                /* for each catalog identifier */
                foreach (var catalogId in catalogIds)
                {
                    CatalogMetadata catalogMetadata;

                    if (_databaseManager.TryReadCatalogMetadata(catalogId, out var jsonString2))
                        catalogMetadata = JsonSerializer.Deserialize<CatalogMetadata>(jsonString2) ?? throw new Exception("catalogMetadata is null");

                    else
                        catalogMetadata = new CatalogMetadata();

                    var catalogContainer = new CatalogContainer(catalogId, user, backendSource, catalogMetadata, this);
                    this.AddCatalogContainer(user, catalogContainer, state.CatalogContainersMap);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to add catalogs from source {Type} and URL {Url}", backendSource.Type, backendSource.ResourceLocator);
            }
        }

        private void AddCatalogContainer(
            ClaimsPrincipal user,
            CatalogContainer catalogContainer, 
            Dictionary<string, List<CatalogContainer>> catalogContainersMap)
        {
            var identity = user.Identity ?? throw new Exception("identity is null");
            var username = identity.Name ?? throw new Exception("name is null");

            /* get common catalog */
            if (!catalogContainersMap.TryGetValue(CatalogManager.CommonCatalogsKey, out var commongCatalogContainers))
            {
                commongCatalogContainers = new List<CatalogContainer>();
                catalogContainersMap[CatalogManager.CommonCatalogsKey] = commongCatalogContainers;
            }

            /* get user specific catalog */
            if (!catalogContainersMap.TryGetValue(username, out var userCatalogContainers))
            {
                userCatalogContainers = new List<CatalogContainer>();
                catalogContainersMap[username] = userCatalogContainers;
            }

            /* common catalog */
            if (AuthorizationUtilities.IsCatalogEditable(user, catalogContainer.Id))
            {
                var index = commongCatalogContainers.FindIndex(current => current.Id == catalogContainer.Id);

                if (index < 0)
                {
                    commongCatalogContainers.Add(catalogContainer);
                }
                else
                {
                    var reference = commongCatalogContainers[index];
                    var otherUser = reference.Owner;

                    /* other user is no admin, but current user is */
                    if (!otherUser.HasClaim(Claims.IS_ADMIN, "true") && user.HasClaim(Claims.IS_ADMIN, "true"))
                        commongCatalogContainers[index] = catalogContainer;
                }
            }
            else
            {
                /* if catalog does not yet exist in common catalog */
                if (!commongCatalogContainers.Any(current => current.Id == catalogContainer.Id))
                {
                    /* if catalog does not yet exist in user catalog */
                    if (!userCatalogContainers.Any(current => current.Id == catalogContainer.Id))
                    {
                        userCatalogContainers.Add(catalogContainer);
                    }
                }
            }
        }

        #endregion
    }
}
