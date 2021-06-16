using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Infrastructure;
using Nexus.Services;
using NJsonSchema.Annotations;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;

namespace Nexus.Controllers
{
    [Route("api/v1/catalogs")]
    [ApiController]
    public class CatalogsController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private UserIdService _userIdService;
        private DatabaseManager _databaseManager;

        #endregion

        #region Constructors

        public CatalogsController(DatabaseManager databaseManager,
                                    UserIdService userIdService,
                                    ILoggerFactory loggerFactory)
        {
            _userIdService = userIdService;
            _databaseManager = databaseManager;
            _logger = loggerFactory.CreateLogger("Nexus");
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets a list of all accessible catalogs.
        /// </summary>
        [HttpGet]
        public ActionResult<List<Catalog>> GetCatalogs()
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            var catalogContainers = _databaseManager.Database.CatalogContainers;

            catalogContainers = catalogContainers.Where(catalogContainer =>
            {
                var isCatalogAccessible = Utilities.IsCatalogAccessible(this.User, catalogContainer.Id, _databaseManager.Database);
                var isCatalogVisible = Utilities.IsCatalogVisible(this.User, catalogContainer.CatalogMeta, isCatalogAccessible);

                return isCatalogAccessible && isCatalogVisible;
            }).ToList();

            return catalogContainers.Select(catalogContainer
                => this.CreateCatalogResponse(catalogContainer.Catalog, catalogContainer.CatalogMeta))
                .ToList();
        }

        /// <summary>
        /// Gets the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        [HttpGet("{catalogId}")]
        public ActionResult<Catalog> GetCatalog(string catalogId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<Catalog>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return this.CreateCatalogResponse(catalog, catalogMeta);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified catalog time range.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        [HttpGet("{catalogId}/timerange")]
        public ActionResult<List<TimeRangeResult>> GetTimeRange(string catalogId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests time range of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<List<TimeRangeResult>>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return this.CreateTimeRangeResponse(catalog);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }


        /// <summary>
        /// Gets the specified catalog availability.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="begin">Start date.</param>
        /// <param name="end">End date.</param>
        /// <param name="granularity">Granularity of the resulting array.</param>
        [HttpGet("{catalogId}/availability")]
        public ActionResult<List<AvailabilityResult>> GetCatalogAvailability(string catalogId,
                                                                             [BindRequired][JsonSchemaDate] DateTime begin,
                                                                             [BindRequired][JsonSchemaDate] DateTime end,
                                                                             [BindRequired] AvailabilityGranularity granularity)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests availability of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<List<AvailabilityResult>>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return this.CreateAvailabilityResponse(catalog, begin, end, granularity);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all channels in the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/channels")]
        public ActionResult<List<Channel>> GetChannels(
            string catalogId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            var remoteIpAddress = this.HttpContext.Connection.RemoteIpAddress;

            // log
            string userName;

            if (this.User.Identity.IsAuthenticated)
                userName = this.User.Identity.Name;
            else
                userName = "anonymous";

            var message = $"User '{userName}' ({remoteIpAddress}) requests channels for catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<List<Channel>>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var channels = catalog.Channels.Select(channel =>
                        {
                            var channelMeta = catalogMeta.Channels.First(
                                current => current.Id == channel.Id);

                            return this.CreateChannelResponse(channel, channelMeta);
                        }).ToList();

                        _logger.LogInformation($"{message} Done.");

                        return channels;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified channel.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="channelId">The channel identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/channels/{channelId}")]
        public ActionResult<Channel> GetChannel(
            string catalogId,
            string channelId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            channelId = WebUtility.UrlDecode(channelId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests channel '{catalogId}/{channelId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<Channel>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var channel = catalog.Channels.FirstOrDefault(
                            current => current.Id.ToString() == channelId);

                        if (channel == null)
                            channel = catalog.Channels.FirstOrDefault(
                                current => current.Name == channelId);

                        if (channel == null)
                            return this.NotFound($"{catalogId}/{channelId}");

                        var channelMeta = catalogMeta.Channels.First(
                            current => current.Id == channel.Id);

                        _logger.LogInformation($"{message} Done.");

                        return this.CreateChannelResponse(channel, channelMeta);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all datasets in the specified catalog and channel.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="channelId">The channel identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/channels/{channelId}/datasets")]
        public ActionResult<List<Dataset>> GetDatasets(
            string catalogId,
            string channelId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            channelId = WebUtility.UrlDecode(channelId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests datasets for channel '{catalogId}/{channelId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<List<Dataset>>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var channel = catalog.Channels.FirstOrDefault(
                            current => current.Id.ToString() == channelId);

                        if (channel == null)
                            channel = catalog.Channels.FirstOrDefault(
                                current => current.Name == channelId);

                        if (channel == null)
                            return this.NotFound($"{catalogId}/{channelId}");

                        _logger.LogInformation($"{message} Done.");

                        return channel.Datasets.Select(dataset 
                            => this.CreateDatasetResponse(dataset))
                            .ToList();
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified dataset.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="channelId">The channel identifier.</param>
        /// <param name="datasetId">The dataset identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/channels/{channelId}/datasets/{datasetId}")]
        public ActionResult<Dataset> GetDataset(
            string catalogId,
            string channelId,
            string datasetId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            channelId = WebUtility.UrlDecode(channelId);
            datasetId = WebUtility.UrlDecode(datasetId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests dataset '{catalogId}/{channelId}/{datasetId}' ...";
            _logger.LogInformation(message);

            try
            {
                return this.ProcessCatalogId<Dataset>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var channel = catalog.Channels.FirstOrDefault(
                            current => current.Id.ToString() == channelId);

                        if (channel == null)
                            channel = catalog.Channels.FirstOrDefault(
                                current => current.Name == channelId);

                        if (channel == null)
                            return this.NotFound($"{catalogId}/{channelId}");

                        var dataset = channel.Datasets.FirstOrDefault(
                           current => current.Id == datasetId);

                        if (dataset == null)
                            return this.NotFound($"{catalogId}/{channelId}/{dataset}");

                        _logger.LogInformation($"{message} Done.");

                        return this.CreateDatasetResponse(dataset);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        private Catalog CreateCatalogResponse(DataModel.Catalog catalog, CatalogMeta catalogMeta)
        {
            return new Catalog()
            {
                Id = catalog.Id,
                Contact = catalogMeta.Contact,
                ShortDescription = catalogMeta.ShortDescription,
                LongDescription = catalogMeta.LongDescription,
                IsQualityControlled = catalogMeta.IsQualityControlled,
                License = catalogMeta.License,
                LogBook = catalogMeta.Logbook
            };
        }

        private List<TimeRangeResult> CreateTimeRangeResponse(DataModel.Catalog catalog)
        {
            var dataReaders = _databaseManager.GetDataReaders(_userIdService.User, catalog.Id);

            return dataReaders.Select(dataReaderForUsing =>
            {
                using var dataReader = dataReaderForUsing;
                (var begin, var end) = dataReader.GetTimeRange(catalog.Id);

                return new TimeRangeResult()
                {
                    Begin = begin,
                    End = end
                };
            }).ToList();
        }

        private List<AvailabilityResult> CreateAvailabilityResponse(DataModel.Catalog catalog, DateTime begin, DateTime end, AvailabilityGranularity granularity)
        {
            var dataReaders = _databaseManager.GetDataReaders(_userIdService.User, catalog.Id);

            return dataReaders.Select(dataReaderForUsing =>
            {
                using var dataReader = dataReaderForUsing;
                var availability = dataReader.GetAvailability(catalog.Id, begin, end, granularity);

                var registration = new DataSourceRegistration()
                {
                    ResourceLocator = availability.DataSourceRegistration.ResourceLocator,
                    DataSourceId = availability.DataSourceRegistration.DataSourceId,
                };

                return new AvailabilityResult()
                {
                    DataSourceRegistration = registration,
                    Data = availability.Data
                };
            }).ToList();
        }

        private Channel CreateChannelResponse(DataModel.Channel channel, ChannelMeta channelMeta)
        {
            return new Channel()
            {
                Id = channel.Id,
                Name = channel.Name,
                Group = channel.Group,
                Unit = !string.IsNullOrWhiteSpace(channelMeta.Unit)
                        ? channelMeta.Unit
                        : channel.Unit,
                Description = !string.IsNullOrWhiteSpace(channelMeta.Description)
                        ? channelMeta.Description
                        : channel.Description,
                SpecialInfo = channelMeta.SpecialInfo
            };
        }

        private Dataset CreateDatasetResponse(DataModel.Dataset dataset)
        {
            return new Dataset()
            {
                Id = dataset.Id,
                DataType = dataset.DataType
            };
        }

        private ActionResult<T> ProcessCatalogId<T>(
            string catalogId,
            string message,
            Func<DataModel.Catalog, CatalogMeta, ActionResult<T>> action)
        {
            if (!Utilities.IsCatalogAccessible(this.User, catalogId, _databaseManager.Database))
                return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

            var catalogContainer = _databaseManager
               .Database
               .CatalogContainers
               .FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer != null)
            {
                var catalog = catalogContainer.Catalog;
                var catalogMeta = catalogContainer.CatalogMeta;

                return action.Invoke(catalog, catalogMeta);
            }
            else
            {
                _logger.LogInformation($"{message} Not found.");
                return this.NotFound(catalogId);
            }
        }

        #endregion

        #region Types

        public record DataSourceRegistration
        {
            public Uri ResourceLocator { get; set; }
            public string DataSourceId { get; set; }
        }

        public record TimeRangeResult
        {
            public DateTime Begin { get; set; }
            public DateTime End { get; set; }
        }

        public record AvailabilityResult
        {
            public DataSourceRegistration DataSourceRegistration { get; set; }
            public Dictionary<DateTime, double> Data { get; set; }
        }

        public record Catalog
        {
            public string Id { get; set; }
            public string Contact { get; set; }
            public string ShortDescription { get; set; }
            public string LongDescription { get; set; }
            public bool IsQualityControlled { get; set; }
            public CatalogLicense License { get;set;}
            public List<string> LogBook { get; set; }
        }

        public record Channel()
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string Group { get; set; }
            public string Unit { get; set; }
            public string Description { get; set; }
            public string SpecialInfo { get; set; }
        }

        public record Dataset()
        {
            public string Id { get; set; }
            public NexusDataType DataType { get; set; }
        }

        #endregion
    }
}
