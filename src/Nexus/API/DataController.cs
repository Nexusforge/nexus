using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Core;
using Nexus.Services;
using Nexus.Core;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading;

namespace Nexus.Controllers
{
    [Route("api/v1/data")]
    [ApiController]
    public class DataController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private UserIdService _userIdService;
        private DatabaseManager _databaseManager;

        #endregion

        #region Constructors

        public DataController(DatabaseManager databaseManager,
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
        /// <param name="channelId">The channel identifier.</param>
        /// <param name="datasetId">The dataset identifier.</param>
        /// <param name="begin">Start date/time.</param>
        /// <param name="end">End date/time.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns></returns>

        [HttpGet]
        public IActionResult GetStream(
            [BindRequired] string catalogId,
            [BindRequired] string channelId,
            [BindRequired] string datasetId,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            CancellationToken cancellationToken)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            channelId = WebUtility.UrlDecode(channelId);
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
                var path = $"{catalogId}/{channelId}/{datasetId}";

                if (!_databaseManager.Database.TryFindDataset(path, out var dataset))
                    return this.NotFound($"Could not find dataset with name '{path}'.");

                var catalog = dataset.Channel.Catalog;

                // security check
                if (!Utilities.IsCatalogAccessible(this.User, catalog.Id, _databaseManager.Database))
                    return this.Unauthorized($"The current user is not authorized to access the catalog '{catalog.Id}'.");

                // dataReader
                using var dataReader = _databaseManager.GetDataSourceController(_userIdService.User, dataset.Registration);

                // read data
                var stream = dataReader.ReadAsDoubleStream(dataset, begin, end, 1 * 1000 * 1000UL, cancellationToken);

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
