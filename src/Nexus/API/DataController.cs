using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to data.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class DataController : ControllerBase
    {
        #region Fields

        private GeneralOptions _generalOptions;
        private AppState _appState;
        private IDataControllerService _dataControllerService;
        private ILoggerFactory _loggerFactory;

        #endregion

        #region Constructors

        public DataController(
            AppState appState,
            IOptions<GeneralOptions> generalOptions,
            IDataControllerService dataControllerService,
            ILoggerFactory loggerFactory)
        {
            _appState = appState;
            _generalOptions = generalOptions.Value;
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
        public async Task<IActionResult> GetStreamAsync(
            [BindRequired] string catalogId,
            [BindRequired] string resourceId,
            [BindRequired] string representationId,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            [BindRequired] CancellationToken cancellationToken)
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

                if (catalogContainer is null || catalogItem is null)
                    return NotFound($"Could not find resource path {resourcePath}.");

                var catalog = catalogItem.Catalog;

                // security check
                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, User))
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to access the catalog {catalog.Id}.");

                // controller
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(
                    catalogContainer.DataSourceRegistration,
                    cancellationToken);

                // read data
                var stream = controller.ReadAsStream(begin, end, catalogItem, _generalOptions, _loggerFactory.CreateLogger<DataSourceController>());

                Response.Headers.ContentLength = stream.Length;
                return File(stream, "application/octet-stream", "data.bin");
            }
            catch (ValidationException ex)
            {
                return UnprocessableEntity(ex.Message);
            }
        }
    }
}
