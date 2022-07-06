using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using Nexus.Utilities;
using System.Security.Claims;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

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

        record CatalogPrototype(
            CatalogRegistration Registration,
            DataSourceRegistration DataSourceRegistration,
            PackageReference PackageReference, 
            ClaimsPrincipal? Owner);

        #endregion

        #region Fields

        private AppState _appState;
        private IDataControllerService _dataControllerService;
        private IDatabaseService _databaseService;
        private IServiceProvider _serviceProvider;
        private IExtensionHive _extensionHive;
        private ILogger<CatalogManager> _logger;

        #endregion

        #region Constructors

        public CatalogManager(
            AppState appState,
            IDataControllerService dataControllerService, 
            IDatabaseService databaseService,
            IServiceProvider serviceProvider,
            IExtensionHive extensionHive,
            ILogger<CatalogManager> logger)
        {
            _appState = appState;
            _dataControllerService = dataControllerService;
            _databaseService = databaseService;
            _serviceProvider = serviceProvider;
            _extensionHive = extensionHive;
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
            if (parent.Id == CatalogContainer.RootCatalogId)
            {
                /* load builtin data source */
                var builtinDataSourceRegistrations = new DataSourceRegistration[]
                {
                    new DataSourceRegistration(
                        Id: Sample.RegistrationId,
                        Type: typeof(Sample).FullName!,
                        ResourceLocator: new Uri("memory://localhost"),
                        Configuration: default)
                };

                /* load all catalog identifiers */
                var path = CatalogContainer.RootCatalogId;
                var catalogPrototypes = new List<CatalogPrototype>();

                /* => for the built-in data source registrations */

#warning Load Parallel?
                /* for each data source registration */
                foreach (var registration in builtinDataSourceRegistrations)
                {
                    using var controller = await _dataControllerService.GetDataSourceControllerAsync(registration, cancellationToken);
                    var catalogRegistrations = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);
                    var packageReference = _extensionHive.GetPackageReference<IDataSource>(registration.Type);

                    foreach (var catalogRegistration in catalogRegistrations)
                    {
                        catalogPrototypes.Add(new CatalogPrototype(
                            catalogRegistration, 
                            registration, 
                            packageReference, 
                            null));
                    }
                }

                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IDBService>();

                /* => for each user with existing config */
                foreach (var (userId, userConfiguration) in _appState.Project.UserConfigurations)
                {
                    // get owner
                    var user = await dbService.FindUserAsync(userId);

                    if (user is null)
                        continue;

                    var claims = user.Claims.Values.Select(claim => new Claim(claim.Type, claim.Value)).ToList();
                    claims.Add(new Claim(Claims.Name, userId));

                    var owner = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            claims,
                            authenticationType: "Fake authentication type",
                            nameType: Claims.Name,
                            roleType: Claims.Role));

                    /* for each data source registration */
                    foreach (var registration in userConfiguration.DataSourceRegistrations.Values)
                    {
                        try
                        {
                            using var controller = await _dataControllerService.GetDataSourceControllerAsync(registration, cancellationToken);
                            var catalogRegistrations = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);
                            var packageReference = _extensionHive.GetPackageReference<IDataSource>(registration.Type);

                            foreach (var catalogRegistration in catalogRegistrations)
                            {
                                catalogPrototypes.Add(new CatalogPrototype(
                                    catalogRegistration, 
                                    registration,
                                    packageReference,
                                    owner));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Unable to process data source registration for user {Username}", user.Name);
                        }
                    }
                }

                catalogContainers = ProcessCatalogPrototypes(catalogPrototypes.ToArray());
                _logger.LogInformation("Found {CatalogCount} top level catalogs", catalogContainers.Length);
            }

            /* all other catalogs */
            else
            {
                using var controller = await _dataControllerService
                    .GetDataSourceControllerAsync(parent.DataSourceRegistration, cancellationToken);

                /* Why trailing slash? 
                 * Because we want the "directory content" (see the "ls /home/karl/" example here:
                 * https://stackoverflow.com/questions/980255/should-a-directory-path-variable-end-with-a-trailing-slash)
                 */

                var catalogRegistrations = await controller
                    .GetCatalogRegistrationsAsync(parent.Id + "/", cancellationToken);

                var prototypes = catalogRegistrations
                    .Select(catalogId => new CatalogPrototype(catalogId, parent.DataSourceRegistration, parent.PackageReference, parent.Owner));

                catalogContainers = ProcessCatalogPrototypes(prototypes.ToArray());
            }

            return catalogContainers;
        }

        private CatalogContainer[] ProcessCatalogPrototypes(
            IEnumerable<CatalogPrototype> catalogPrototypes)
        {
            /* clean up */
            catalogPrototypes = EnsureNoHierarchy(catalogPrototypes);

            /* convert to catalog containers */
            var catalogContainers = catalogPrototypes.Select(prototype =>
            {
                /* create catalog metadata */
                CatalogMetadata catalogMetadata;

                if (_databaseService.TryReadCatalogMetadata(prototype.Registration.Path, out var jsonString2))
                    catalogMetadata = JsonSerializer.Deserialize<CatalogMetadata>(jsonString2) ?? throw new Exception("catalogMetadata is null");

                else
                    catalogMetadata = new CatalogMetadata(default, default, default);

                /* create catalog container */
                var catalogContainer = new CatalogContainer(
                    prototype.Registration,
                    prototype.Owner,
                    prototype.DataSourceRegistration,
                    prototype.PackageReference,
                    catalogMetadata,
                    this,
                    _databaseService,
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
            // it is required that a catalog that comes from a certain data source can only have child
            // catalogs of the very same data source.
            // 
            // In general, child catalogs will be loaded lazily. Therefore, for any catalog of the provided array that
            // appears to be a child catalog, it can be assumed it comes from a data source other than the one
            // from the parent catalog. Depending on the user's rights, this method decides which one will survive.
            // 
            //
            // Example:
            //
            // The following combination of catalogs is allowed:
            // data source 1: /a + /a/a + /a/b
            // data source 2: /a2/c
            //
            // The following combination of catalogs is forbidden:
            // data source 1: /a + /a/a + /a/b
            // data source 2: /a/c

            var catalogPrototypesToKeep = new List<CatalogPrototype>();

            foreach (var catalogPrototype in catalogPrototypes)
            {
                var referenceIndex = catalogPrototypesToKeep.FindIndex(
                    current =>
                        {
                            var currentCatalogId = current.Registration.Path + '/';
                            var prototypeCatalogId = catalogPrototype.Registration.Path + '/';

                            return currentCatalogId.StartsWith(prototypeCatalogId, StringComparison.OrdinalIgnoreCase) ||
                                   prototypeCatalogId.StartsWith(currentCatalogId, StringComparison.OrdinalIgnoreCase);
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
                    var ownerCanWrite = owner is null || AuthorizationUtilities.IsCatalogWritable(catalogPrototype.Registration.Path, owner);

                    var otherPrototype = catalogPrototypesToKeep[referenceIndex];
                    var otherOwner = otherPrototype.Owner;
                    var otherOwnerCanWrite = otherOwner is null || AuthorizationUtilities.IsCatalogWritable(otherPrototype.Registration.Path, otherOwner);

                    if (!otherOwnerCanWrite && ownerCanWrite)
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
