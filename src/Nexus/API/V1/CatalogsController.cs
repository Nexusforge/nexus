using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Controllers.V1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class CatalogsController : ControllerBase
    {
        #region Fields

        private AppState _appState;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public CatalogsController(
            AppState appState,
            IDataControllerService dataControllerService)
        {
            _appState = appState;
            _dataControllerService = dataControllerService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets a list of all accessible catalog identifiers.
        /// </summary>
        [HttpGet]
        public ActionResult<string[]> GetCatalogIds()
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            var catalogContainers = _appState.CatalogState.CatalogContainers;

            var response = catalogContainers
                .Where(catalogContainer => AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, this.User))
                .Select(catalogContainer => catalogContainer.Id)
                .ToArray();

            return response;
        }

        /// <summary>
        /// Gets the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}")]
        public async Task<ActionResult<ResourceCatalog>> 
            GetCatalog(
                string catalogId,
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            return await this.ProcessCatalogIdAsync(catalogId, catalog =>
            {
                return Task.FromResult((ActionResult<ResourceCatalog>)this.CreateCatalogResponse(catalog));
            }, cancellationToken);
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

            return await this.ProcessCatalogIdAsync<TimeRangeResult[]>(catalogId, async catalog =>
            {
                return await this.CreateTimeRangeResponseAsync(catalog, cancellationToken);
            }, cancellationToken);
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
                [BindRequired] DateTime begin,
                [BindRequired] DateTime end,
                [BindRequired] AvailabilityGranularity granularity,
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            return await this.ProcessCatalogIdAsync<AvailabilityResult[]>(catalogId, async catalog =>
            {
                return await this.CreateAvailabilityResponseAsync(catalog, begin, end, granularity, cancellationToken);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets a list of all resources in the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources")]
        public async Task<ActionResult<Resource[]>> GetResources(
            [BindRequired] string catalogId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress;

            return await this.ProcessCatalogIdAsync(catalogId, catalog =>
            {
                var resources = catalog.Resources
                    .Select(resource => this.CreateResourceResponse(resource))
                    .ToArray();

                return Task.FromResult((ActionResult<Resource[]>)resources);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets the specified resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}")]
        public async Task<ActionResult<Resource>> GetResource(
            string catalogId,
            string resourceId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            return await this.ProcessCatalogIdAsync(catalogId, catalog =>
            {
                var resource = catalog.Resources.FirstOrDefault(
                    current => current.Id.ToString() == resourceId);

                if (resource == null)
                    resource = catalog.Resources.FirstOrDefault(
                        current => current.Id == resourceId);

                if (resource == null)
                    return Task.FromResult((ActionResult<Resource>)this.NotFound($"{catalogId}/{resourceId}"));

                return Task.FromResult((ActionResult<Resource>)this.CreateResourceResponse(resource));
            }, cancellationToken);
        }

        /// <summary>
        /// Gets a list of all representations in the specified catalog and resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations")]
        public async Task<ActionResult<Representation[]>> GetRepresentations(
            string catalogId,
            string resourceId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            return await this.ProcessCatalogIdAsync(catalogId, catalog =>
            {
                var resource = catalog.Resources.FirstOrDefault(
                    current => current.Id.ToString() == resourceId);

                if (resource == null)
                    resource = catalog.Resources.FirstOrDefault(
                        current => current.Id == resourceId);

                if (resource == null)
                    return Task.FromResult((ActionResult<Representation[]>)this.NotFound($"{catalogId}/{resourceId}"));

                var response = resource.Representations.Select(representation
                    => this.CreateRepresentationResponse(representation))
                    .ToArray();

                return Task.FromResult((ActionResult<Representation[]>)response);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets the specified representation.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="representationId">The representation identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations/{representationId}")]
        public async Task<ActionResult<Representation>> GetRepresentation(
            string catalogId,
            string resourceId,
            string representationId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            representationId = WebUtility.UrlDecode(representationId);

            // log
            return await this.ProcessCatalogIdAsync<Representation>(catalogId, catalog =>
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

                return Task.FromResult((ActionResult<Representation>)this.CreateRepresentationResponse(representation));
            }, cancellationToken);
        }

        private ResourceCatalog CreateCatalogResponse(ResourceCatalog catalog)
        {
            return catalog;
        }

        private async Task<TimeRangeResult[]> 
            CreateTimeRangeResponseAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
        {
            var tasks = _appState.CatalogState.BackendSourceToCatalogIdsMap
                .Where(entry => entry.Value.Any(id => id == catalog.Id))
                .Select(async entry =>
                {
                    using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(entry.Key, cancellationToken);
                    var timeRange = await dataSource.GetTimeRangeAsync(catalog.Id, cancellationToken);

                    var backendSource = new BackendSource(
                        Type: timeRange.BackendSource.Type,
                        ResourceLocator: timeRange.BackendSource.ResourceLocator);

                    return new TimeRangeResult(
                        BackendSource: backendSource,
                        Begin: timeRange.Begin,
                        End: timeRange.End);
                }).ToList();

            var timeRangeResults = await Task
                 .WhenAll(tasks);

            return timeRangeResults;
        }

        private async Task<AvailabilityResult[]> 
            CreateAvailabilityResponseAsync(ResourceCatalog catalog, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            var tasks = _appState.CatalogState.BackendSourceToCatalogIdsMap
               .Where(entry => entry.Value.Any(id => id == catalog.Id))
               .Select(async entry =>
               {
                    using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(entry.Key, cancellationToken);
                    var availability = await dataSource.GetAvailabilityAsync(catalog.Id, begin, end, granularity, cancellationToken);

                    var backendSource = new BackendSource(
                        Type: availability.BackendSource.Type, 
                        ResourceLocator: availability.BackendSource.ResourceLocator);

                    return new AvailabilityResult(
                        BackendSource: backendSource,
                        Data: availability.Data);
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
            Func<ResourceCatalog, Task<ActionResult<T>>> action,
            CancellationToken cancellationToken)
        {
            var catalogContainer = _appState.CatalogState.CatalogContainers
               .FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer != null)
            {
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, this.User))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

                return await action.Invoke(catalogInfo.Catalog);
            }
            else
            {
                return this.NotFound(catalogId);
            }
        }

        #endregion
    }
}
