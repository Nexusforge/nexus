using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using NJsonSchema.Annotations;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Controllers
{
    [Route("api/v1/catalogs")]
    [ApiController]
    public class CatalogsController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private UserIdService _userIdService;
        private IDatabaseManager _databaseManager;

        #endregion

        #region Constructors

        public CatalogsController(IDatabaseManager databaseManager,
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
                var isCatalogAccessible = NexusUtilities.IsCatalogAccessible(this.User, catalogContainer.Id, _databaseManager.Database);
                var isCatalogVisible = NexusUtilities.IsCatalogVisible(this.User, catalogContainer.CatalogSettings, isCatalogAccessible);

                return isCatalogAccessible && isCatalogVisible;
            }).ToList();

            var response = catalogContainers.Select(catalogContainer
                => this.CreateCatalogResponse(catalogContainer.Catalog, catalogContainer.CatalogSettings))
                .ToList();

            return response;
        }

        /// <summary>
        /// Gets the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        [HttpGet("{catalogId}")]
        public async Task<ActionResult<Catalog>> GetCatalog(string catalogId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return Task.FromResult((ActionResult<Catalog>)this.CreateCatalogResponse(catalog, catalogMeta));
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
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/timerange")]
        public async Task<ActionResult<TimeRangeResult[]>> 
            GetTimeRange(
                string catalogId, 
                CancellationToken cancellationToken)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests time range of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<TimeRangeResult[]>(catalogId, message,
                    async (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return await this.CreateTimeRangeResponseAsync(catalog, cancellationToken);
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
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{catalogId}/availability")]
        public async Task<ActionResult<List<AvailabilityResult>>> 
            GetCatalogAvailability(
                [BindRequired] string catalogId,
                [BindRequired][JsonSchemaDate] DateTime begin,
                [BindRequired][JsonSchemaDate] DateTime end,
                [BindRequired] AvailabilityGranularity granularity,
                CancellationToken cancellationToken)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests availability of catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<AvailabilityResult[]>(catalogId, message,
                    async (catalog, catalogMeta) =>
                    {
                        _logger.LogInformation($"{message} Done.");
                        return await this.CreateAvailabilityResponseAsync(catalog, begin, end, granularity, cancellationToken);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all resources in the specified catalog.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources")]
        public async Task<ActionResult<List<Resource>>> GetResources(
            [BindRequired] string catalogId)
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

            var message = $"User '{userName}' ({remoteIpAddress}) requests resources for catalog '{catalogId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var resources = catalog.Resources.Select(resource =>
                        {
                            var resourceMeta = catalogMeta.Resources.First(
                                current => current.Id == resource.Id);

                            return this.CreateResourceResponse(resource, resourceMeta);
                        }).ToList();

                        _logger.LogInformation($"{message} Done.");

                        return Task.FromResult((ActionResult<List<Resource>>)resources);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}")]
        public async Task<ActionResult<Resource>> GetResource(
            string catalogId,
            string resourceId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests resource '{catalogId}/{resourceId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var resource = catalog.Resources.FirstOrDefault(
                            current => current.Id.ToString() == resourceId);

                        if (resource == null)
                            resource = catalog.Resources.FirstOrDefault(
                                current => current.Name == resourceId);

                        if (resource == null)
                            return Task.FromResult((ActionResult<Resource>)this.NotFound($"{catalogId}/{resourceId}"));

                        var resourceMeta = catalogMeta.Resources.First(
                            current => current.Id == resource.Id);

                        _logger.LogInformation($"{message} Done.");

                        return Task.FromResult((ActionResult<Resource>)this.CreateResourceResponse(resource, resourceMeta));
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets a list of all representations in the specified catalog and resource.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations")]
        public async Task<ActionResult<List<Representation>>> GetRepresentations(
            string catalogId,
            string resourceId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests representations for resource '{catalogId}/{resourceId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var resource = catalog.Resources.FirstOrDefault(
                            current => current.Id.ToString() == resourceId);

                        if (resource == null)
                            resource = catalog.Resources.FirstOrDefault(
                                current => current.Name == resourceId);

                        if (resource == null)
                            return Task.FromResult((ActionResult<List<Representation>>)this.NotFound($"{catalogId}/{resourceId}"));

                        _logger.LogInformation($"{message} Done.");

                        var response = resource.Representations.Select(representation 
                            => this.CreateRepresentationResponse(representation))
                            .ToList();

                        return Task.FromResult((ActionResult<List<Representation>>)response);
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        /// <summary>
        /// Gets the specified representation.
        /// </summary>
        /// <param name="catalogId">The catalog identifier.</param>
        /// <param name="resourceId">The resource identifier.</param>
        /// <param name="representationId">The representation identifier.</param>
        /// <returns></returns>
        [HttpGet("{catalogId}/resources/{resourceId}/representations/{representationId}")]
        public async Task<ActionResult<Representation>> GetRepresentation(
            string catalogId,
            string resourceId,
            string representationId)
        {
            if (_databaseManager.Database == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            catalogId = WebUtility.UrlDecode(catalogId);
            resourceId = WebUtility.UrlDecode(resourceId);
            representationId = WebUtility.UrlDecode(representationId);

            // log
            var message = $"User '{_userIdService.GetUserId()}' requests representation '{catalogId}/{resourceId}/{representationId}' ...";
            _logger.LogInformation(message);

            try
            {
                return await this.ProcessCatalogIdAsync<Representation>(catalogId, message,
                    (catalog, catalogMeta) =>
                    {
                        var resource = catalog.Resources.FirstOrDefault(
                            current => current.Id.ToString() == resourceId);

                        if (resource == null)
                            resource = catalog.Resources.FirstOrDefault(
                                current => current.Name == resourceId);

                        if (resource == null)
                            return Task.FromResult((ActionResult<Representation>)this.NotFound($"{catalogId}/{resourceId}"));

                        var representation = resource.Representations.FirstOrDefault(
                           current => current.Id == representationId);

                        if (representation == null)
                            return Task.FromResult((ActionResult<Representation>)this.NotFound($"{catalogId}/{resourceId}/{representation}"));

                        _logger.LogInformation($"{message} Done.");

                        return Task.FromResult((ActionResult<Representation>)this.CreateRepresentationResponse(representation));
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{message} {ex.GetFullMessage()}");
                throw;
            }
        }

        private Catalog CreateCatalogResponse(DataModel.ResourceCatalog catalog, CatalogProperties catalogMeta)
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

        private async Task<TimeRangeResult[]> 
            CreateTimeRangeResponseAsync(DataModel.ResourceCatalog catalog, CancellationToken cancellationToken)
        {
            var dataSources = await _databaseManager.GetDataSourcesAsync(_userIdService.User, catalog.Id, cancellationToken);

            var tasks = dataSources.Select(async dataSourceForUsing =>
            {
                using var dataSource = dataSourceForUsing;
                var timeRange = await dataSource.GetTimeRangeAsync(catalog.Id, cancellationToken);

                var backendSource = new BackendSource()
                {
                    Type = timeRange.BackendSource.Type,
                    ResourceLocator = timeRange.BackendSource.ResourceLocator,
                };

                return new TimeRangeResult()
                {
                    BackendSource = backendSource,
                    Begin = timeRange.Begin,
                    End = timeRange.End
                };
            }).ToList();

            var timeRangeResults = await Task
                 .WhenAll(tasks);

            return timeRangeResults;
        }

        private async Task<AvailabilityResult[]> 
            CreateAvailabilityResponseAsync(DataModel.ResourceCatalog catalog, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            var dataSources = await _databaseManager.GetDataSourcesAsync(_userIdService.User, catalog.Id, cancellationToken);

            var tasks = dataSources.Select(async dataSourceForUsing =>
            {
                using var dataSource = dataSourceForUsing;
                var availability = await dataSource.GetAvailabilityAsync(catalog.Id, begin, end, granularity, cancellationToken);

                var backendSource = new BackendSource()
                {
                    ResourceLocator = availability.BackendSource.ResourceLocator,
                    Type = availability.BackendSource.Type,
                };

                return new AvailabilityResult()
                {
                    BackendSource = backendSource,
                    Data = availability.Data
                };
            }).ToList();

            var availabilityResults = await Task.WhenAll(tasks);

            return availabilityResults;
        }

        private Resource CreateResourceResponse(DataModel.Resource resource, ResourceMeta resourceMeta)
        {
            return new Resource()
            {
                Id = resource.Id,
                Name = resource.Name,
                Group = resource.Group,
                Unit = !string.IsNullOrWhiteSpace(resourceMeta.Unit)
                        ? resourceMeta.Unit
                        : resource.Unit,
                Description = !string.IsNullOrWhiteSpace(resourceMeta.Description)
                        ? resourceMeta.Description
                        : resource.Description,
                SpecialInfo = resourceMeta.SpecialInfo
            };
        }

        private Representation CreateRepresentationResponse(DataModel.Representation representation)
        {
            return new Representation()
            {
                Id = representation.Id,
                DataType = representation.DataType
            };
        }

        private async Task<ActionResult<T>> ProcessCatalogIdAsync<T>(
            string catalogId,
            string message,
            Func<DataModel.ResourceCatalog, CatalogProperties, Task<ActionResult<T>>> action)
        {
            if (!NexusUtilities.IsCatalogAccessible(this.User, catalogId, _databaseManager.Database))
                return this.Unauthorized($"The current user is not authorized to access the catalog '{catalogId}'.");

            var catalogContainer = _databaseManager
               .Database
               .CatalogContainers
               .FirstOrDefault(container => container.Id == catalogId);

            if (catalogContainer != null)
            {
                var catalog = catalogContainer.Catalog;
                var catalogMeta = catalogContainer.CatalogSettings;

                return await action.Invoke(catalog, catalogMeta);
            }
            else
            {
                _logger.LogInformation($"{message} Not found.");
                return this.NotFound(catalogId);
            }
        }

        #endregion

        #region Types

        public record BackendSource
        {
            public string Type { get; set; }
            public Uri ResourceLocator { get; set; }
        }

        public record AvailabilityResult
        {
            public BackendSource BackendSource { get; set; }
            public Dictionary<DateTime, double> Data { get; set; }
        }

        public record TimeRangeResult
        {
            public BackendSource BackendSource { get; set; }
            public DateTime Begin { get; set; }
            public DateTime End { get; set; }
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

        public record Resource()
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string Group { get; set; }
            public string Unit { get; set; }
            public string Description { get; set; }
            public string SpecialInfo { get; set; }
        }

        public record Representation()
        {
            public string Id { get; set; }
            public NexusDataType DataType { get; set; }
        }

        #endregion
    }
}
