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
                using var controller = await _dataControllerService.GetDataSourceControllerForDataAccessAsync(
                    this.User,
                    catalogItem.Representation.BackendSource,
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
