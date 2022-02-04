using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Models;
using Nexus.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogContainer[]> GetCatalogContainersAsync(
            CatalogContainer parent,
            CancellationToken cancellationToken);
    }

    internal class CatalogManager : ICatalogManager
    {
        #region Types

        record CatalogPrototype(CatalogRegistration Registration, BackendSource BackendSource, ClaimsPrincipal Owner);

        #endregion

        #region Fields

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

        public async Task<CatalogContainer[]> GetCatalogContainersAsync(
            CatalogContainer parent,
            CancellationToken cancellationToken)
        {
            CatalogContainer[] catalogContainers;

            /* special case: root */
            if (parent.Id == "/")
            {
                /* load builtin backend source */
                var builtinBackendSources = new BackendSource[]
                {
                    new BackendSource(
                        Type: typeof(InMemory).FullName ?? throw new Exception("full name is null"),
                        ResourceLocator: new Uri("memory://localhost"),
                        Configuration: new Dictionary<string, string>(),
                        Publish: true),
                };

                /* load all catalog identifiers */
                var path = "/";
                var catalogPrototypes = new List<CatalogPrototype>();

                /* => for the built-in backend sources */
                var rootUser = await _userManagerWrapper.GetClaimsPrincipalAsync(_securityOptions.RootUser);

#warning Load Parallel?
                if (rootUser is not null)
                {
                    /* for each backend source */
                    foreach (var backendSource in builtinBackendSources)
                    {
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogRegistrations = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);

                        foreach (var catalogRegistration in catalogRegistrations)
                        {
                            catalogPrototypes.Add(new CatalogPrototype(catalogRegistration, backendSource, rootUser));
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
                        using var controller = await _dataControllerService.GetDataSourceControllerAsync(backendSource, cancellationToken);
                        var catalogIds = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);

                        foreach (var catalogId in catalogIds)
                        {
                            catalogPrototypes.Add(new CatalogPrototype(catalogId, backendSource, user));
                        }
                    }
                }

                catalogContainers = this.ProcessCatalogPrototypes(catalogPrototypes.ToArray());
                _logger.LogInformation("Found {CatalogCount} top level catalogs.", catalogContainers.Length);
            }

            /* all other catalogs */
            else
            {
                using var controller = await _dataControllerService
                    .GetDataSourceControllerAsync(parent.BackendSource, cancellationToken);

                var catalogRegistrations = await controller
                    .GetCatalogRegistrationsAsync(parent.Id + "/", cancellationToken);

                var prototypes = catalogRegistrations
                    .Select(catalogId => new CatalogPrototype(catalogId, parent.BackendSource, parent.Owner));

                catalogContainers = this.ProcessCatalogPrototypes(prototypes.ToArray());
            }

            return catalogContainers;
        }

        private CatalogContainer[] ProcessCatalogPrototypes(
            IEnumerable<CatalogPrototype> catalogPrototypes)
        {
            /* clean up */
            catalogPrototypes = this.EnsureNoHierarchy(catalogPrototypes);

            /* convert to catalog containers */
            var catalogContainers = catalogPrototypes.Select(prototype =>
            {
                /* create catalog metadata */
                CatalogMetadata catalogMetadata;

                if (_databaseManager.TryReadCatalogMetadata(prototype.Registration.Path, out var jsonString2))
                    catalogMetadata = JsonSerializer.Deserialize<CatalogMetadata>(jsonString2) ?? throw new Exception("catalogMetadata is null");

                else
                    catalogMetadata = new CatalogMetadata(default, default, default, default);

                /* create catalog container */
                var catalogContainer = new CatalogContainer(
                    prototype.Registration,
                    prototype.Owner,
                    prototype.BackendSource,
                    catalogMetadata,
                    this,
                    _databaseManager,
                    _dataControllerService);

                return catalogContainer;
            });

            return catalogContainers.ToArray();
        }

        private CatalogPrototype[] EnsureNoHierarchy(
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
            // Backend source 2: /a2/c
            //
            // The following combination of catalogs is forbidden:
            // Backend source 1: /a + /a/a + /a/b
            // Backend source 2: /a/c

            var catalogPrototypesToKeep = new List<CatalogPrototype>();

            foreach (var catalogPrototype in catalogPrototypes)
            {
                var referenceIndex = catalogPrototypesToKeep.FindIndex(
                    current =>
                        {
                            var currentCatalogId = current.Registration.Path + '/';
                            var prototypeCatalogId = catalogPrototype.Registration.Path + '/';

                            return currentCatalogId.StartsWith(prototypeCatalogId) ||
                                   prototypeCatalogId.StartsWith(currentCatalogId);
                        });

                /* nothing found */
                if (referenceIndex < 0)
                {
                    catalogPrototypesToKeep.Add(catalogPrototype);
                }

                /* reference found */
                else
                {
                    var owner = catalogPrototype.Owner;
                    var otherOwner = catalogPrototypesToKeep[referenceIndex].Owner;

                    /* other user is no admin, but current user is */
                    if (!otherOwner.HasClaim(Claims.IS_ADMIN, "true") && owner.HasClaim(Claims.IS_ADMIN, "true"))
                    {
                        _logger.LogWarning("Duplicate catalog {CatalogId}", catalogPrototypesToKeep[referenceIndex]);
                        catalogPrototypesToKeep[referenceIndex] = catalogPrototype;
                    }
                }
            }

            return catalogPrototypesToKeep.ToArray();
        }

        #endregion
    }
}
