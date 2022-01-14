using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Nexus.Controllers.V1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class DataController : ControllerBase
    {
        #region Fields

        private AppState _appState;
        private IDataControllerService _dataControllerService;
        private ILoggerFactory _loggerFactory;

        #endregion

        #region Constructors

        public DataController(
            AppState appState,
            IDataControllerService dataControllerService,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _dataControllerService = dataControllerService;
            _loggerFactory = loggerFactory;
        }

        #endregion

        /// <summary>
        /// Gets the requested data.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="representationId">The representation identifier.</param>
        /// <param name="begin">Start date/time.</param>
        /// <param name="end">End date/time.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns></returns>

        [HttpGet]
        public async Task<IActionResult> GetStream(
            [BindRequired] string catalogId,
            [BindRequired] string resourceId,
            [BindRequired] string representationId,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            CancellationToken cancellationToken)
        {
            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            representationId = WebUtility.UrlDecode(representationId);

            begin = DateTime.SpecifyKind(begin, DateTimeKind.Utc);
            end = DateTime.SpecifyKind(end, DateTimeKind.Utc);

            try
            {
                // find representation
                var root = _appState.CatalogState.Root;

                var resourcePath = $"{catalogId}/{resourceId}/{representationId}";

                var (catalogContainer, catalogItem) = await root.TryFindAsync(resourcePath, cancellationToken);

                if (catalogItem is null)
                    return this.NotFound($"Could not find resource path {resourcePath}.");

                var catalog = catalogItem.Catalog;

                // security check
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.User))
                    return this.Unauthorized($"The current user is not authorized to access the catalog {catalog.Id}.");

                // controller
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(
                    catalogContainer.BackendSource,
                    cancellationToken);

                // read data
                var stream = controller.ReadAsStream(begin, end, catalogItem, _loggerFactory.CreateLogger<DataSourceController>());

                this.Response.Headers.ContentLength = stream.Length;
                return this.File(stream, "application/octet-stream", "data.bin");
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            }
        }
    }
}
