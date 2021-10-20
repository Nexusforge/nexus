using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using NJsonSchema.Annotations;
using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Controllers
{
    [Route("api/v1/catalogs")]
    [ApiController]
    public class CatalogsController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private AppState _appState;
        private UserIdService _userIdService;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public CatalogsController(
            AppState appState,
            UserIdService userIdService,
            IDataControllerService dataControllerService,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _userIdService = userIdService;
            _dataControllerService = dataControllerService;
            _logger = loggerFactory.CreateLogger("Nexus");
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets a list of all accessible catalogs.
        /// </summary>
        [HttpGet]
        public ActionResult<ResourceCatalog[]> GetCatalogs()
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            var catalogContainers = _appState.CatalogState.CatalogCollection.CatalogContainers;

            catalogContainers = catalogContainers.Where(catalogContainer =>
            {
                var isCatalogAccessible = AuthorizationUtilities.IsCatalogAccessible(this.User, catalogContainer);
                var isCatalogVisible = AuthorizationUtilities.IsCatalogVisible(this.User, catalogContainer, isCatalogAccessible);

                return isCatalogAccessible && isCatalogVisible;
            }).ToList();

            var response = catalogContainers.Select(catalogContainer
                => this.CreateCatalogResponse(catalogContainer.Catalog))
                .ToArray();

            return response;
        }

        /// <summary>
        /// Gets the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        [HttpGet("{catalogId}")]
        public async Task<ActionResult<ResourceCatalog>> GetCatalog(string catalogId)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message, catalog =>
                {
                    _logger.LogInformation($"{message} Done.");
                    return Task.FromResult((ActionResult<ResourceCatalog>)this.CreateCatalogResponse(catalog));
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified catalog time range.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/timerange")]
        public async Task<ActionResult<TimeRangeResult[]>> 
            GetTimeRange(
                string catalogId, 
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests time range of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<TimeRangeResult[]>(catalogId, message, async catalog =>
                {
                    _logger.LogInformation($"{message} Done.");
                    return await this.CreateTimeRangeResponseAsync(catalog, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified catalog availability.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="begin">Start date.</param>
        /// <param name="end">End date.</param>
        /// <param name="granularity">Granularity of the resulting array.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/availability")]
        public async Task<ActionResult<AvailabilityResult[]>>
            GetCatalogAvailability(
                [BindRequired] string catalogId,
                [BindRequired][JsonSchemaDate] DateTime begin,
                [BindRequired][JsonSchemaDate] DateTime end,
                [BindRequired] AvailabilityGranularity granularity,
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests availability of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<AvailabilityResult[]>(catalogId, message, async catalog =>
                {
                    _logger.LogInformation($"{message} Done.");
                    return await this.CreateAvailabilityResponseAsync(catalog, begin, end, granularity, cancellationToken);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all resources in the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources")]
        public async Task<ActionResult<Resource[]>> GetResources(
            [BindRequired] string catalogId)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress;

            // log
            string userName;

            if (this.User.Identity.IsAuthenticated)
                userName = this.User.Identity.Name;
            else
                userName = "anonymous";

            var message = $"User '{userName}' ({remoteIpAddress}) requests resources for catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message, catalog =>
                {
                    var resources = catalog.Resources
                        .Select(resource => this.CreateResourceResponse(resource))
                        .ToArray();

                    _logger.LogInformation($"{message} Done.");

                    return Task.FromResult((ActionResult<Resource[]>)resources);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}")]
        public async Task<ActionResult<Resource>> GetResource(
            string catalogId,
            string resourceId)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests resource '{catalogId}/{resourceId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message, catalog =>
                {
                    var resource = catalog.Resources.FirstOrDefault(
                        current => current.Id.ToString() == resourceId);

                    if (resource == null)
                        resource = catalog.Resources.FirstOrDefault(
                            current => current.Id == resourceId);

                    if (resource == null)
                        return Task.FromResult((ActionResult<Resource>)this.NotFound($"{catalogId}/{resourceId}"));

                    _logger.LogInformation($"{message} Done.");

                    return Task.FromResult((ActionResult<Resource>)this.CreateResourceResponse(resource));
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all representations in the specified catalog and resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations")]
        public async Task<ActionResult<Representation[]>> GetRepresentations(
            string catalogId,
            string resourceId)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests representations for resource '{catalogId}/{resourceId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message, catalog =>
                {
                    var resource = catalog.Resources.FirstOrDefault(
                        current => current.Id.ToString() == resourceId);

                    if (resource == null)
                        resource = catalog.Resources.FirstOrDefault(
                            current => current.Id == resourceId);

                    if (resource == null)
                        return Task.FromResult((ActionResult<Representation[]>)this.NotFound($"{catalogId}/{resourceId}"));

                    _logger.LogInformation($"{message} Done.");

                    var response = resource.Representations.Select(representation 
                        => this.CreateRepresentationResponse(representation))
                        .ToArray();

                    return Task.FromResult((ActionResult<Representation[]>)response);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified representation.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="representationId">The representation identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations/{representationId}")]
        public async Task<ActionResult<Representation>> GetRepresentation(
            string catalogId,
            string resourceId,
            string representationId)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            representationId = WebUtility.UrlDecode(representationId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests representation '{catalogId}/{resourceId}/{representationId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<Representation>(catalogId, message, catalog =>
                {
                    var resource = catalog.Resources.FirstOrDefault(
                        current => current.Id.ToString() == resourceId);

                    if (resource == null)
                        resource = catalog.Resources.FirstOrDefault(
                            current => current.Id == resourceId);

                    if (resource == null)
                        return Task.FromResult((ActionResult<Representation>)this.NotFound($"{catalogId}/{resourceId}"));

                    var representation = resource.Representations.FirstOrDefault(
                        current => current.Id == representationId);

                    if (representation == null)
                        return Task.FromResult((ActionResult<Representation>)this.NotFound($"{catalogId}/{resourceId}/{representation}"));

                    _logger.LogInformation($"{message} Done.");

                    return Task.FromResult((ActionResult<Representation>)this.CreateRepresentationResponse(representation));
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        private ResourceCatalog CreateCatalogResponse(ResourceCatalog catalog)
        {
            return catalog;
        }

        private async Task<TimeRangeResult[]> 
            CreateTimeRangeResponseAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
        {
            var tasks = _appState.CatalogState.BackendSourceToCatalogsMap
                .Where(entry => entry.Value.Any(catalog => catalog.Id == catalog.Id))
                .Select(async entry =>
                {
                    using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(entry.Key, cancellationToken);
                    var timeRange = await dataSource.GetTimeRangeAsync(catalog.Id, cancellationToken);

                    var backendSource = new BackendSource()
                    {
                        Type = timeRange.BackendSource.Type,
                        ResourceLocator = timeRange.BackendSource.ResourceLocator,
                    };

                    return new TimeRangeResult()
                    {
                        BackendSource = backendSource,
                        Begin = timeRange.Begin,
                        End = timeRange.End
                    };
                }).ToList();

            var timeRangeResults = await Task
                 .WhenAll(tasks);

            return timeRangeResults;
        }

        private async Task<AvailabilityResult[]> 
            CreateAvailabilityResponseAsync(ResourceCatalog catalog, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            var tasks = _appState.CatalogState.BackendSourceToCatalogsMap
               .Where(entry => entry.Value.Any(catalog => catalog.Id == catalog.Id))
               .Select(async entry =>
               {
                   using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(entry.Key, cancellationToken);
                   var availability = await dataSource.GetAvailabilityAsync(catalog.Id, begin, end, granularity, cancellationToken);

                    var backendSource = new BackendSource()
                    {
                        ResourceLocator = availability.BackendSource.ResourceLocator,
                        Type = availability.BackendSource.Type,
                    };

                    return new AvailabilityResult()
                    {
                        BackendSource = backendSource,
                        Data = availability.Data
                    };
                }).ToList();

            var availabilityResults = await Task.WhenAll(tasks);

            return availabilityResults;
        }

        private Resource CreateResourceResponse(Resource resource)
        {
            return resource;
        }

        private Representation CreateRepresentationResponse(Representation representation)
        {
            return representation;
        }

        private async Task<ActionResult<T>> ProcessCatalogIdAsync<T>(
            string catalogId,
            string message,
            Func<ResourceCatalog, Task<ActionResult<T>>> action)
        {
            var catalogContainer = _appState.CatalogState.CatalogCollection.CatalogContainers
               .FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer != null)
            {
                if (!AuthorizationUtilities.IsCatalogAccessible(this.User, catalogContainer))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

                var catalog = catalogContainer.Catalog;

                return await action.Invoke(catalog);
            }
            else
            {
                _logger.LogInformation($"{message} Not found.");
                return this.NotFound(catalogId);
            }
        }

        #endregion
    }
}
