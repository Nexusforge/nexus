using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.DataModel;
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
            GetCatalogAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            return await this.ProcessCatalogIdAsync<ResourceCatalog>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);
                return this.CreateCatalogResponse(catalogInfo.Catalog);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets the specified catalog time range.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/timerange")]
        public async Task<ActionResult<TimeRangeResponse>> 
            GetTimeRangeAsync(
                string catalogId, 
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            return await this.ProcessCatalogIdAsync<TimeRangeResponse>(catalogId, async catalogContainer =>
            {
                return await this.CreateTimeRangeResponseAsync(catalogContainer, cancellationToken);
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
        public async Task<ActionResult<AvailabilityResponse>>
            GetCatalogAvailabilityAsync(
                [BindRequired] string catalogId,
                [BindRequired] DateTime begin,
                [BindRequired] DateTime end,
                CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            return await this.ProcessCatalogIdAsync<AvailabilityResponse>(catalogId, async catalog =>
            {
                return await this.CreateAvailabilityResponseAsync(catalog, begin, end, cancellationToken);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets a list of all resources in the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources")]
        public async Task<ActionResult<Resource[]>> GetResourcesAsync(
            [BindRequired] string catalogId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress;

            return await this.ProcessCatalogIdAsync<Resource[]>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

                var resources = catalogInfo.Catalog.Resources
                    .Select(resource => this.CreateResourceResponse(resource))
                    .ToArray();

                return resources;
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
        public async Task<ActionResult<Resource>> GetResourceAsync(
            string catalogId,
            string resourceId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            return await this.ProcessCatalogIdAsync<Resource>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

                var resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                    current => current.Id.ToString() == resourceId);

                if (resource == null)
                    resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                        current => current.Id == resourceId);

                if (resource == null)
                    return this.NotFound($"{catalogId}/{resourceId}");

                return this.CreateResourceResponse(resource);
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
        public async Task<ActionResult<Representation[]>> GetRepresentationsAsync(
            string catalogId,
            string resourceId,
            CancellationToken cancellationToken)
        {
            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            return await this.ProcessCatalogIdAsync<Representation[]>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

                var resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                    current => current.Id.ToString() == resourceId);

                if (resource == null)
                    resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                        current => current.Id == resourceId);

                if (resource == null)
                    return this.NotFound($"{catalogId}/{resourceId}");

                var response = resource.Representations.Select(representation
                    => this.CreateRepresentationResponse(representation))
                    .ToArray();

                return response;
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
        public async Task<ActionResult<Representation>> GetRepresentationAsync(
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
            return await this.ProcessCatalogIdAsync<Representation>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);

                var resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                    current => current.Id.ToString() == resourceId);

                if (resource == null)
                    resource = catalogInfo.Catalog.Resources.FirstOrDefault(
                        current => current.Id == resourceId);

                if (resource == null)
                    return this.NotFound($"{catalogId}/{resourceId}");

                var representation = resource.Representations.FirstOrDefault(
                    current => current.Id == representationId);

                if (representation == null)
                    return this.NotFound($"{catalogId}/{resourceId}/{representation}");

                return this.CreateRepresentationResponse(representation);
            }, cancellationToken);
        }

        private ResourceCatalog CreateCatalogResponse(ResourceCatalog catalog)
        {
            return catalog;
        }

        private async Task<TimeRangeResponse> 
            CreateTimeRangeResponseAsync(CatalogContainer catalogContainer, CancellationToken cancellationToken)
        {
            using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.BackendSource, cancellationToken);
            var timeRange = await dataSource.GetTimeRangeAsync(catalogContainer.Id, cancellationToken);

            var timeRangeResult = new TimeRangeResponse(
                Begin: timeRange.Begin,
                End: timeRange.End);

            return timeRangeResult;
        }

        private async Task<AvailabilityResponse> 
            CreateAvailabilityResponseAsync(CatalogContainer catalogContainer, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.BackendSource, cancellationToken);
            var availability = await dataSource.GetAvailabilityAsync(catalogContainer.Id, begin, end, cancellationToken);

            var availabilityResults = new AvailabilityResponse(
                Data: availability.Data);

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
            Func<CatalogContainer, Task<ActionResult<T>>> action,
            CancellationToken cancellationToken)
        {
            var catalogContainer = _appState.CatalogState.CatalogContainers
               .FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer is not null)
            {
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, this.User))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

                return await action.Invoke(catalogContainer);
            }
            else
            {
                return this.NotFound(catalogId);
            }
        }

        #endregion
    }
}
