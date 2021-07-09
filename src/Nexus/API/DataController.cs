using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.ComponentModel.DataAnnotations;
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
        private UserIdService _userIdService;
        private IDatabaseManager _databaseManager;

        #endregion

        #region Constructors

        public DataController(IDatabaseManager databaseManager,
                              UserIdService userIdService,
                              ILoggerFactory loggerFactory)
        {
            _databaseManager = databaseManager;
            _userIdService = userIdService;
            _logger = loggerFactory.CreateLogger("Nexus");
        }

        #endregion

        /// <summary>
        /// Gets the requested data.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="datasetId">The dataset identifier.</param>
        /// <param name="begin">Start date/time.</param>
        /// <param name="end">End date/time.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns></returns>

        [HttpGet]
        public async Task<IActionResult> GetStream(
            [BindRequired] string catalogId,
            [BindRequired] string resourceId,
            [BindRequired] string datasetId,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            CancellationToken cancellationToken)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            datasetId = WebUtility.UrlDecode(datasetId);

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
                // dataset
                var path = $"{catalogId}/{resourceId}/{datasetId}";

                if (!_databaseManager.Database.TryFind(path, out var datasetRecord))
                    return this.NotFound($"Could not find dataset on path '{path}'.");

                var catalog = datasetRecord.Catalog;

                // security check
                if (!NexusUtilities.IsCatalogAccessible(this.User, catalog.Id, _databaseManager.Database))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalog.Id}'.");

                // controller
                using var controller = await _databaseManager.GetDataSourceControllerAsync(
                    _userIdService.User, 
                    datasetRecord.Dataset.BackendSource,
                    cancellationToken);

                // read data
                var stream = controller.ReadAsStream(begin, end, datasetRecord);

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
