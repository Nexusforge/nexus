using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Data;
using System.Net;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to catalogs.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class CatalogsController : ControllerBase
    {
        // GET      /api/catalogs/{catalogId}
        // GET      /api/catalogs/{catalogId}/child-catalog-infos
        // GET      /api/catalogs/{catalogId}/timerange
        // GET      /api/catalogs/{catalogId}/availability
        // GET      /api/catalogs/{catalogId}/attachments
        // GET      /api/catalogs/{catalogId}/attachments/{attachmentId}/content

        // GET      /api/catalogs/{catalogId}/metadata
        // PUT      /api/catalogs/{catalogId}/metadata

        #region Fields

        private AppState _appState;
        private IDatabaseService _databaseService;
        private IDataControllerService _dataControllerService;

        #endregion

        #region Constructors

        public CatalogsController(
            AppState appState,
            IDatabaseService databaseService,
            IDataControllerService dataControllerService)
        {
            _appState = appState;
            _databaseService = databaseService;
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
            GetAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = ProtectCatalogByAccessibilityAsync<ResourceCatalog>(catalogId, checkAccess: true, async catalogContainer =>
            {
                var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);
                return lazyCatalogInfo.Catalog;
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets a list of child catalog info for the provided parent catalog identifier.
        /// </summary>
        /// <param name="catalogId">The parent catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/child-catalog-infos")]
        public async Task<ActionResult<CatalogInfo[]>>
            GetChildCatalogInfosAsync(
            string catalogId, 
            CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = await ProtectCatalogByAccessibilityAsync<CatalogInfo[]>(catalogId, checkAccess: false, async catalogContainer =>
            {
                var childContainers = await catalogContainer.GetChildCatalogContainersAsync(cancellationToken);

                return childContainers
                    .Select(childContainer =>
                    {
                        var id = childContainer.Id;
                        var title = childContainer.Title;
                        var contact = childContainer.Metadata.Contact;
                        var isReadable = AuthorizationUtilities.IsCatalogReadable(childContainer.Id, childContainer.Metadata, childContainer.Owner, User);
                        var isWritable = AuthorizationUtilities.IsCatalogWritable(childContainer.Id, User);
                        var isPublished = childContainer.Owner is null || AuthorizationUtilities.IsCatalogWritable(childContainer.Id, childContainer.Owner);
                        var isOwner = childContainer.Owner?.FindFirstValue(Claims.Subject) == User.FindFirstValue(Claims.Subject);

                        string? license = default!;

                        if (_databaseService.TryReadAttachment(childContainer.Id, "LICENSE.md", out var attachment))
                            license = new StreamReader(attachment).ReadToEnd();

                        return new CatalogInfo(
                            id,
                            title,
                            contact,
                            license,
                            isReadable,
                            isWritable,
                            isPublished,
                            isOwner
                        );
                    })
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
        public Task<ActionResult<CatalogTimeRange>>
            GetTimeRangeAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = ProtectCatalogByAccessibilityAsync<CatalogTimeRange>(catalogId, checkAccess: true, async catalogContainer =>
            {
                using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.DataSourceRegistration, cancellationToken);
                return await dataSource.GetTimeRangeAsync(catalogContainer.Id, cancellationToken);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets the specified catalog availability.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="begin">Start date/time.</param>
        /// <param name="end">End date/time.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/availability")]
        public Task<ActionResult<CatalogAvailability>>
            GetAvailabilityAsync(
                string catalogId,
                DateTime begin,
                DateTime end,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = ProtectCatalogByAccessibilityAsync<CatalogAvailability>(catalogId, checkAccess: true, async catalogContainer =>
            {
                using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.DataSourceRegistration, cancellationToken);
                return await dataSource.GetAvailabilityAsync(catalogContainer.Id, begin, end, cancellationToken);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Gets all attachments for the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/attachments")]
        public Task<ActionResult<string[]>>
            GetAttachmentsAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            try
            {
                var response = ProtectCatalogByAccessibilityAsync<string[]>(catalogId, checkAccess: true, catalog =>
                {
                    return Task.FromResult((ActionResult<string[]>)_databaseService.EnumerateAttachments(catalogId).ToArray());
                }, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                return Task.FromResult((ActionResult<string[]>)
                    UnprocessableEntity(ex.Message));
            }
        }

        /// <summary>
        /// Gets the specified attachment.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="attachmentId">The attachment identifier.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/attachments/{attachmentId}/content")]
        public Task<ActionResult>
            GetAttachmentStreamAsync(
                string catalogId,
                string attachmentId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);
            attachmentId = WebUtility.UrlDecode(attachmentId);

            var response = ProtectCatalogByAccessibilityNonGenericAsync(catalogId, catalog =>
            {
                try
                {
                    if (_databaseService.TryReadAttachment(catalogId, attachmentId, out var attachmentStream))
                    {
                        Response.Headers.ContentLength = attachmentStream.Length;
                        return Task.FromResult((ActionResult)
                            File(attachmentStream, "application/octet-stream", attachmentId));
                    }
                    else
                    {
                        return Task.FromResult((ActionResult)
                            NotFound($"Could not find attachment {attachmentId} for catalog {catalogId}."));
                    }
                }
                catch (Exception ex)
                {
                    return Task.FromResult((ActionResult)
                        UnprocessableEntity(ex.Message));
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
            GetMetadataAsync(
                string catalogId,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = ProtectCatalogByAccessibilityAsync<CatalogMetadata>(catalogId, checkAccess: true, async catalogContainer =>
            {
                return await Task.FromResult(catalogContainer.Metadata);
            }, cancellationToken);

            return response;
        }

        /// <summary>
        /// Puts the catalog metadata.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="catalogMetadata">The catalog metadata to put.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpPut("{catalogId}/metadata")]
        public Task
            PutMetadataAsync(
                string catalogId,
                [FromBody] CatalogMetadata catalogMetadata,
                CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);

            var response = ProtectCatalogByAccessibilityAsync<object>(catalogId, checkAccess: true, async catalogContainer =>
            {
                var canEdit = AuthorizationUtilities.IsCatalogWritable(catalogId, User);

                if (!canEdit)
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to modify the catalog {catalogId}.");
                
                await catalogContainer.UpdateMetadataAsync(catalogMetadata);

                return new object();

            }, cancellationToken);

            return response;
        }

        private async Task<ActionResult<T>> ProtectCatalogByAccessibilityAsync<T>(
            string catalogId,
            bool checkAccess,
            Func<CatalogContainer, Task<ActionResult<T>>> action,
            CancellationToken cancellationToken)
        {
            var root = _appState.CatalogState.Root;
            
            var catalogContainer = catalogId == CatalogContainer.RootCatalogId
                ? root
                : await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is not null)
            {
                if (checkAccess && !AuthorizationUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to access the catalog {catalogId}.");

                return await action.Invoke(catalogContainer);
            }
            else
            {
                return NotFound(catalogId);
            }
        }

        private async Task<ActionResult> ProtectCatalogByAccessibilityNonGenericAsync(
            string catalogId,
            Func<CatalogContainer, Task<ActionResult>> action,
            CancellationToken cancellationToken)
        {
            var root = _appState.CatalogState.Root;
            var catalogContainer = await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

            if (catalogContainer is not null)
            {
                if (!AuthorizationUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to access the catalog {catalogId}.");

                return await action.Invoke(catalogContainer);
            }
            else
            {
                return NotFound(catalogId);
            }
        }

        #endregion
    }
}
