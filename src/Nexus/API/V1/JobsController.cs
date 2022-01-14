using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

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
        private Serilog.IDiagnosticContext _diagnosticContext;
        private AppState _appState;
        private JobService<ExportJob> _exportJobService;

        #endregion

        #region Constructors

        public JobsController(
            AppState appState,
            JobService<ExportJob> exportJobService,
            Serilog.IDiagnosticContext diagnosticContext,
            IServiceProvider serviceProvider,
            ILogger<JobsController> logger)
        {
            _appState = appState;
            _serviceProvider = serviceProvider;
            _exportJobService = exportJobService;
            _diagnosticContext = diagnosticContext;
            _logger = logger;
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
#warning Reenable
            throw new NotImplementedException();

            //            _diagnosticContext.Set("Body", JsonSerializerHelper.SerializeIntended(parameters));

            //            parameters.Begin = parameters.Begin.ToUniversalTime();
            //            parameters.End = parameters.End.ToUniversalTime();

            //            var root = _appState.CatalogState.Root;

            //            // translate resource paths to representations
            //            CatalogItem[] catalogItems;

            //            try
            //            {
            //                catalogItems = await Task.WhenAll(parameters.ResourcePaths.Select(async resourcePath =>
            //                {
            //                    var catalogItem = await root.TryFindAsync(resourcePath, cancellationToken);

            //                    if (catalogItem is null)
            //                        throw new ValidationException($"Could not find resource path {resourcePath}.");

            //                    return catalogItem;
            //                }));
            //            }
            //            catch (ValidationException ex)
            //            {
            //                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            //            }

            //            // check that there is anything to export
            //            if (!catalogItems.Any())
            //                return this.BadRequest("The list of resource paths is empty.");

            //            // build up catalog items map and authorize
            //            Dictionary<CatalogContainer, IEnumerable<CatalogItem>> catalogItemsMap;

            //            try
            //            {
            //                catalogItemsMap = catalogItems
            //                    .GroupBy(catalogItem => catalogItem.Catalog.Id)
            //                    .ToDictionary(
            //                        group =>
            //                        {
            //                            var catalogContainer = catalogContainers.First(catalogContainer => catalogContainer.Id == group.Key);

            //                            if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.HttpContext.User))
            //                                throw new UnauthorizedAccessException($"The current user is not authorized to access catalog '{catalogContainer.Id}'.");

            //                            return catalogContainer;
            //                        },
            //                        group => (IEnumerable<CatalogItem>)group);

            //            }
            //            catch (UnauthorizedAccessException ex)
            //            {
            //                return this.Unauthorized(ex.Message);
            //            }

            //            //
            //            var job = new ExportJob(
            //                Parameters: parameters)
            //            {
            //                Owner = this.User.Identity.Name
            //            };

            //            var dataService = _serviceProvider.GetRequiredService<DataService>();

            //            try
            //            {
            //                var jobControl = _exportJobService.AddJob(job, dataService.ReadProgress, async (jobControl, cts) =>
            //                {
            //                    var userIdService = _serviceProvider.GetRequiredService<UserIdService>();
            //#warning ExportId should be ASP Request ID!
            //                    var exportId = Guid.NewGuid();

            //                    return await dataService.ExportAsync(parameters, catalogItemsMap, exportId, cts.Token);
            //                });

            //                return this.Accepted($"{this.GetBasePath()}{this.Request.Path}/{jobControl.Job.Id}/status", jobControl.Job);
            //            }
            //            catch (ValidationException ex)
            //            {
            //                return this.UnprocessableEntity(ex.GetFullMessage(includeStackTrace: false));
            //            }
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
                    jobControl.Job.Owner is null ||
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
                    jobControl.Job.Owner is null ||
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
