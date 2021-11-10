using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Controllers.V1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class JobsController : ControllerBase
    {
        #region Fields

        private ILogger _logger;
        private IServiceProvider _serviceProvider;
        private IDataControllerService _dataControllerService;
        private Serilog.IDiagnosticContext _diagnosticContext;
        private AppState _appState;
        private JobService<ExportJob> _exportJobService;
        private JobService<AggregationJob> _aggregationJobService;
        private PathsOptions _pathsOptions;

        #endregion

        #region Constructors

        public JobsController(
            AppState appState,
            JobService<ExportJob> exportJobService,
            JobService<AggregationJob> aggregationJobService,
            IDataControllerService dataControllerService,
            Serilog.IDiagnosticContext diagnosticContext,
            IServiceProvider serviceProvider,
            ILogger<JobsController> logger,
            IOptions<PathsOptions> pathOptions)
        {
            _appState = appState;
            _serviceProvider = serviceProvider;
            _exportJobService = exportJobService;
            _dataControllerService = dataControllerService;
            _diagnosticContext = diagnosticContext;
            _aggregationJobService = aggregationJobService;
            _logger = logger;
            _pathsOptions = pathOptions.Value;
        }

        #endregion

        #region Export Jobs

        /// <summary>
        /// Creates a new export job.
        /// </summary>
        /// <param name="parameters">Export parameters.</param>
        /// <param name="cancellationToken">The token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpPost("export")]
        public async Task<ActionResult<ExportJob>> CreateExportJobAsync(
            ExportParameters parameters,
            CancellationToken cancellationToken)
        {
            _diagnosticContext.Set("Body", JsonSerializerHelper.Serialize(parameters));

            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            parameters.Begin = parameters.Begin.ToUniversalTime();
            parameters.End = parameters.End.ToUniversalTime();

            // translate resource paths to representations
            CatalogItem[] catalogItems;

            var state = _appState.CatalogState;

            try
            {
                catalogItems = await Task.WhenAll(parameters.ResourcePaths.Select(async resourcePath =>
                {
                    CatalogItem catalogItem;

                    if ((catalogItem = await state.CatalogContainers.TryFindAsync(resourcePath, cancellationToken)) == null)
                        throw new ValidationException($"Could not find resource path {resourcePath}.");

                    return catalogItem;
                }));
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            }

            // check that there is anything to export
            if (!catalogItems.Any())
                return this.BadRequest("The list of resource paths is empty.");

            // authorize
            var catalogIds = catalogItems.Select(catalogItem => catalogItem.Catalog.Id).Distinct();

            foreach (var catalogId in catalogIds)
            {
                var catalogContainer = state.CatalogContainers
                    .First(container => container.Id == catalogId);

                if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, this.HttpContext.User))
                    return this.Unauthorized($"The current user is not authorized to access catalog '{catalogId}'.");
            }

            //
            var job = new ExportJob(
                Parameters: parameters)
            {
                Owner = this.User.Identity.Name
            };

            var dataService = _serviceProvider.GetRequiredService<DataService>();

            try
            {
                var jobControl = _exportJobService.AddJob(job, dataService.ReadProgress, async (jobControl, cts) =>
                {
                    var userIdService = _serviceProvider.GetRequiredService<UserIdService>();
#warning ExportId should be ASP Request ID!
                    var exportId = Guid.NewGuid();

                    return await dataService.ExportAsync(parameters, catalogItems, exportId, cts.Token);
                });

                return this.Accepted($"{this.GetBasePath()}{this.Request.Path}/{jobControl.Job.Id}/status", jobControl.Job);
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            }
        }

        /// <summary>
        /// Gets a list of all export jobs.
        /// </summary>
        /// <returns></returns>
        [HttpGet("export")]
        public ActionResult<List<ExportJob>> GetExportJobs()
        {
            return _exportJobService
                .GetJobs()
                .Select(jobControl => jobControl.Job)
                .ToList();
        }

        /// <summary>
        /// Gets the specified export job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("export/{jobId}")]
        public ActionResult<ExportJob> GetExportJob(Guid jobId)
        {
            if (_exportJobService.TryGetJob(jobId, out var jobControl))
                return jobControl.Job;
            else
                return this.NotFound(jobId);
        }

        /// <summary>
        /// Gets the status of the specified export job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("export/{jobId}/status")]
        public ActionResult<JobStatus> GetExportJobStatus(Guid jobId)
        {
            if (_exportJobService.TryGetJob(jobId, out var jobControl))
            {
                if (this.User.Identity.Name == jobControl.Job.Owner ||
                    jobControl.Job.Owner == null ||
                    this.User.HasClaim(Claims.IS_ADMIN, "true"))
                {
                    return new JobStatus(
                        Start: jobControl.Start,
                        Progress: jobControl.Progress,
                        Status: jobControl.Task.Status,
                        ExceptionMessage: jobControl.Task.Exception is not null
                            ? jobControl.Task.Exception.GetFullMessage(includeStackTrace: false)
                            : string.Empty,
                        Result: jobControl.Task.Status == TaskStatus.RanToCompletion
                            ? $"{this.GetBasePath()}/{jobControl.Task.Result}"
                            : null);
                }
                else
                {
                    return this.Unauthorized($"The current user is not authorized to access the status of job '{jobControl.Job.Id}'.");
                }
            }
            else
            {
                return this.NotFound(jobId);
            }
        }

        /// <summary>
        /// Cancels the specified job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpDelete("export/{jobId}")]
        public ActionResult DeleteExportJob(Guid jobId)
        {
            if (_exportJobService.TryGetJob(jobId, out var jobControl))
            {
                if (this.User.Identity.Name == jobControl.Job.Owner || 
                    jobControl.Job.Owner == null ||
                    this.User.HasClaim(Claims.IS_ADMIN, "true"))
                {
                    jobControl.CancellationTokenSource.Cancel();
                    return this.Accepted();
                }
                else
                {
                    return this.Unauthorized($"The current user is not authorized to cancel the job '{jobControl.Job.Id}'.");
                }
            }
            else
            {
                return this.NotFound(jobId);
            }
        }

        #endregion

        #region Aggregation Jobs

        /// <summary>
        /// Creates a new aggregation job.
        /// </summary>
        /// <param name="setup">Aggregation setup.</param>
        /// <returns></returns>
        [HttpPost("aggregation")]
        public ActionResult<AggregationJob> CreateAggregationJob(AggregationSetup setup)
        {
            _diagnosticContext.Set("Body", JsonSerializerHelper.Serialize(setup));

            if (_appState.CatalogState == null)
                return this.StatusCode(503, "The database has not been loaded yet.");

            setup.Begin = setup.Begin.ToUniversalTime();
            setup.End = setup.End.ToUniversalTime();

            // authorize
            if (!this.User.HasClaim(Claims.IS_ADMIN, "true"))
                return this.Unauthorized($"The current user is not authorized to create an aggregation job.");

            //
            var job = new AggregationJob(
                Setup: setup)
            {
                Owner = this.User.Identity.Name
            };

            var aggregationService = _serviceProvider.GetRequiredService<AggregationService>();
            var databaseManager = _serviceProvider.GetRequiredService<IDatabaseManager>();

            try
            {
                var jobControl = _aggregationJobService.AddJob(job, aggregationService.Progress, (jobControl, cts) =>
                {
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await aggregationService.AggregateDataAsync(
                                _pathsOptions.Cache,
                                setup,
                                _appState.CatalogState,
                                backendSource => _dataControllerService.GetDataSourceControllerAsync(backendSource, cts.Token),
                                cts.Token);

                            return result;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Aggregation failed");
                            throw;
                        }
                    });

                    return task;
                });

                return this.Accepted($"{this.GetBasePath()}{this.Request.Path}/{jobControl.Job.Id}/status", jobControl.Job);
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            }
        }

        /// <summary>
        /// Gets a list of all aggregation jobs.
        /// </summary>
        /// <returns></returns>
        [HttpGet("aggregation")]
        public ActionResult<List<AggregationJob>> GetAggregationJobs()
        {
            return _aggregationJobService
                .GetJobs()
                .Select(jobControl => jobControl.Job)
                .ToList();
        }

        /// <summary>
        /// Gets the specified aggregation job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("aggregation/{jobId}")]
        public ActionResult<AggregationJob> GetAggregationJob(Guid jobId)
        {
            if (_aggregationJobService.TryGetJob(jobId, out var jobControl))
                return jobControl.Job;
            else
                return this.NotFound(jobId);
        }

        /// <summary>
        /// Gets the status of the specified export job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("aggregation/{jobId}/status")]
        public ActionResult<JobStatus> GetAggregationJobStatus(Guid jobId)
        {
            if (_aggregationJobService.TryGetJob(jobId, out var jobControl))
            {
                if (this.User.Identity.Name == jobControl.Job.Owner ||
                    jobControl.Job.Owner == null ||
                    this.User.HasClaim(Claims.IS_ADMIN, "true"))
                {
                    return new JobStatus(
                        Start: jobControl.Start,
                        Progress: jobControl.Progress,
                        Status: jobControl.Task.Status,
                        ExceptionMessage: jobControl.Task.Exception is not null
                            ? jobControl.Task.Exception.GetFullMessage(includeStackTrace: false)
                            : string.Empty,
                        Result: jobControl.Task.Status == TaskStatus.RanToCompletion
                            ? jobControl.Task.Result
                            : null);
                }
                else
                {
                    return this.Unauthorized($"The current user is not authorized to access the status of job '{jobControl.Job.Id}'.");
                }
            }
            else
            {
                return this.NotFound(jobId);
            }
        }

        /// <summary>
        /// Cancels the specified job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpDelete("aggregation/{jobId}")]
        public ActionResult DeleteAggregationJob(Guid jobId)
        {
            // security check
            if (!this.User.HasClaim(Claims.IS_ADMIN, "true"))
                return this.Unauthorized($"The current user is not authorized to cancel aggregation jobs.");

            if (_exportJobService.TryGetJob(jobId, out var jobControl))
            {
                if (this.User.Identity.Name == jobControl.Job.Owner ||
                    jobControl.Job.Owner == null ||
                    this.User.HasClaim(Claims.IS_ADMIN, "true"))
                {
                    jobControl.CancellationTokenSource.Cancel();
                    return this.Accepted();
                }
                else
                {
                    return this.Unauthorized($"The current user is not authorized to cancel the job '{jobControl.Job.Id}'.");
                }
            }
            else
            {
                return this.NotFound(jobId);
            }
        }

        #endregion

        #region Methods

        private string GetBasePath()
        {
            return $"{this.Request.Scheme}://{this.Request.Host}";
        }

        #endregion
    }
}
