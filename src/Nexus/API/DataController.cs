using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Controllers
{
    [Route("api/v1/data")]
    [ApiController]
    public class DataController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private IDataControllerService _dataControllerService;
        private AppState _appState;

        #endregion

        #region Constructors

        public DataController(AppState appState,
                              IDataControllerService dataControllerService,
                              ILogger<DataController> logger)
        {
            _appState = appState;
            _dataControllerService = dataControllerService;
            _logger = logger;
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
            if (_appState.CatalogState is null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            representationId = WebUtility.UrlDecode(representationId);

            var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress;

            // log
            string userName;

            if (this.User.Identity.IsAuthenticated)
                userName = this.User.Identity.Name;
            else
                userName = "anonymous";

            begin = DateTime.SpecifyKind(begin, DateTimeKind.Utc);
            end = DateTime.SpecifyKind(end, DateTimeKind.Utc);

            var message = $"User '{userName}' ({remoteIpAddress}) streams data: {begin.ToISO8601()} to {end.ToISO8601()} ...";
            _logger.LogInformation(message);

            try
            {
                var catalogCollection = _appState.CatalogState.CatalogCollection;

                // representation
                var path = $"{catalogId}/{resourceId}/{representationId}";

                if (!catalogCollection.TryFind(path, out var catalogItem))
                    return this.NotFound($"Could not find representation on path '{path}'.");

                var catalog = catalogItem.Catalog;

#warning better would be to get container directly
                var container = catalogCollection.CatalogContainers.First(container => container.Id == catalog.Id);

                // security check
                if (!AuthorizationUtilities.IsCatalogAccessible(this.User, container))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalog.Id}'.");

                // controller
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(
                    catalogItem.Representation.BackendSource,
                    cancellationToken);

                // read data
                var stream = controller.ReadAsStream(begin, end, catalogItem, _logger);

                _logger.LogInformation($"{message} Done.");

                this.Response.Headers.ContentLength = stream.Length;
                return this.File(stream, "application/octet-stream", "data.bin");
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }
    }
}
