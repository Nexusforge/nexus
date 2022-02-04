﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Models;
using Nexus.Services;
using Nexus.Utilities;
using System.Data;
using System.Net;

namespace Nexus.Controllers.V1
{
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class CatalogsController : ControllerBase
    {
        // GET      /api/catalogs/{catalogId}
        // GET      /api/catalogs/{catalogId}/child-catalog-ids
        // GET      /api/catalogs/{catalogId}/timerange
        // GET      /api/catalogs/{catalogId}/availability
        // GET      /api/catalogs/{catalogId}/attachments/{attachmentId}/content

        // GET      /api/catalogs/{catalogId}/metadata
        // PUT      /api/catalogs/{catalogId}/metadata

        #region Fields

        private AppState _appState;
        private IDatabaseManager _databaseManager;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public CatalogsController(
            AppState appState,
            IDatabaseManager databaseManager,
            IDataControllerService dataControllerService)
        {
            _appState = appState;
            _databaseManager = databaseManager;
            _dataControllerService = dataControllerService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}")]
        public Task<ActionResult<ResourceCatalog>>
            GetCatalogAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = this.ProcessCatalogIdAsync<ResourceCatalog>(catalogId, async catalogContainer =>
            {
                var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);
                return catalogInfo.Catalog;
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets a list of child catalog identifiers for the provided parent catalog identifier.
        /// <param name="catalogId">The parent catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        /// </summary>
        [HttpGet("{catalogId}/child-catalog-ids")]
        public async Task<ActionResult<string[]>>
            GetChildCatalogIdsAsync(
            string catalogId, 
            CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = await this.ProcessCatalogIdAsync<string[]>(catalogId, async catalogContainer =>
            {
                var catalogContainers = await catalogContainer.GetChildCatalogContainersAsync(cancellationToken);

                return catalogContainers
                    .Where(catalogContainer => AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.User))
                    .Select(catalogContainer => catalogContainer.Id)
                    .ToArray();
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets the specified catalog's time range.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/timerange")]
        public Task<ActionResult<TimeRangeResponse>>
            GetTimeRangeAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = this.ProcessCatalogIdAsync<TimeRangeResponse>(catalogId, async catalogContainer =>
            {
                using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.BackendSource, cancellationToken);
                return await dataSource.GetTimeRangeAsync(catalogContainer.Id, cancellationToken);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets the specified catalog availability.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="begin">Start date.</param>
        /// <param name="end">End date.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/availability")]
        public Task<ActionResult<AvailabilityResponse>>
            GetCatalogAvailabilityAsync(
                string catalogId,
                DateTime begin,
                DateTime end,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = this.ProcessCatalogIdAsync<AvailabilityResponse>(catalogId, async catalogContainer =>
            {
                using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.BackendSource, cancellationToken);
                return await dataSource.GetAvailabilityAsync(catalogContainer.Id, begin, end, cancellationToken);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets the specified attachment.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="attachmentId">The attachment identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/attachments/{attachmentId}/content")]
        public Task<ActionResult>
            DownloadAttachementAsync(
                string catalogId,
                string attachmentId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);
            attachmentId = WebUtility.UrlDecode(attachmentId);

            var response = this.ProcessCatalogIdNonGenericAsync(catalogId, catalog =>
            {
                if (_databaseManager.TryReadAttachment(catalogId, attachmentId, out var attachementStream))
                {
                    this.Response.Headers.ContentLength = attachementStream.Length;
                    return Task.FromResult((ActionResult)
                        this.File(attachementStream, "application/octet-stream", attachmentId));
                }
                else
                {
                    return Task.FromResult((ActionResult)
                        this.NotFound($"Could not find attachment {attachmentId} for catalog {catalogId}."));
                }
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets the catalog metadata.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/metadata")]
        public Task<ActionResult<CatalogMetadata>>
            GetCatalogMetadataAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = this.ProcessCatalogIdAsync<CatalogMetadata>(catalogId, async catalogContainer =>
            {
                return await Task.FromResult(catalogContainer.Metadata);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Puts the catalog metadata.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="request">The set catalog metadata request.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpPut("{catalogId}/metadata")]
        public Task
            PutCatalogMetadataAsync(
                string catalogId,
                [FromBody] SetMetadataRequest request,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = this.ProcessCatalogIdAsync<object>(catalogId, async catalogContainer =>
            {
                var canEdit = this.User.HasClaim(Claims.CAN_EDIT_CATALOG, "true");

                if (!canEdit)
                    return this.Unauthorized($"The current user is not authorized to modify the catalog '{catalogId}'.");
                
                await catalogContainer.UpdateMetadataAsync(request.Metadata);

                return new object();

            }, cancellationToken);

            return response;
        }

        private async Task<ActionResult<T>> ProcessCatalogIdAsync<T>(
            string catalogId,
            Func<CatalogContainer, Task<ActionResult<T>>> action,
            CancellationToken cancellationToken)
        {
            var root = _appState.CatalogState.Root;

            var catalogContainer = catalogId == root.Id
                ? root
                : await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is not null)
            {
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.User))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

                return await action.Invoke(catalogContainer);
            }
            else
            {
                return this.NotFound(catalogId);
            }
        }

        private async Task<ActionResult> ProcessCatalogIdNonGenericAsync(
            string catalogId,
            Func<CatalogContainer, Task<ActionResult>> action,
            CancellationToken cancellationToken)
        {
            var root = _appState.CatalogState.Root;
            var catalogContainer = await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is not null)
            {
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.User))
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
