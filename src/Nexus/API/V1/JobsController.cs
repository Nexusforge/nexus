using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Controllers.V1
{
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class JobsController : ControllerBase
    {
        #region Fields

        private AppStateManager _appStateManager;
        private ILogger _logger;
        private IServiceProvider _serviceProvider;
        private Serilog.IDiagnosticContext _diagnosticContext;
        private IJobService _jobService;

        #endregion

        #region Constructors

        public JobsController(
            AppStateManager appStateManager,
            IJobService jobService,
            IServiceProvider serviceProvider,
            Serilog.IDiagnosticContext diagnosticContext,
            ILogger<JobsController> logger)
        {
            _appStateManager = appStateManager;
            _jobService = jobService;
            _serviceProvider = serviceProvider;
            _diagnosticContext = diagnosticContext;
            _logger = logger;
        }

        #endregion

        #region Export

        /// <summary>
        /// Creates a new export job.
        /// </summary>
        /// <param name="parameters">Export parameters.</param>
        /// <param name="cancellationToken">The token to cancel the current operation.</param>
        /// <returns></returns>
        [HttpPost("export")]
        public async Task<ActionResult<Job>> ExportAsync(
            ExportParameters parameters,
            CancellationToken cancellationToken)
        {
            _diagnosticContext.Set("Body", JsonSerializerHelper.SerializeIntended(parameters));

            parameters.Begin = parameters.Begin.ToUniversalTime();
            parameters.End = parameters.End.ToUniversalTime();

            var root = _appStateManager.AppState.CatalogState.Root;

            // translate resource paths to representations
            (CatalogContainer Container, CatalogItem Item)[] catalogContainsAndItems;

            try
            {
                catalogContainsAndItems = await Task.WhenAll(parameters.ResourcePaths.Select(async resourcePath =>
                {
                    var (catalogContainer, catalogItem) = await root.TryFindAsync(resourcePath, cancellationToken);

                    if (catalogItem is null)
                        throw new ValidationException($"Could not find resource path {resourcePath}.");

                    return (catalogContainer, catalogItem);
                }));
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.Message);
            }

            // check that there is anything to export
            if (!catalogContainsAndItems.Any())
                return this.BadRequest("The list of resource paths is empty.");

            // build up catalog items map and authorize
            Dictionary<CatalogContainer, IEnumerable<CatalogItem>> catalogItemsMap;

            try
            {
                catalogItemsMap = catalogContainsAndItems
                    .GroupBy(current => current.Container.Id)
                    .ToDictionary(
                        group =>
                        {
                            var catalogContainer = group.First().Container;

                            if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, this.HttpContext.User))
                                throw new UnauthorizedAccessException($"The current user is not authorized to access catalog '{catalogContainer.Id}'.");

                            return catalogContainer;
                        },
                        group => (IEnumerable<CatalogItem>)group);

            }
            catch (UnauthorizedAccessException ex)
            {
                return this.Unauthorized(ex.Message);
            }

            //
            var job = new Job(Guid.NewGuid(), "export", this.User.Identity.Name, parameters);
            var dataService = _serviceProvider.GetRequiredService<DataService>();

            try
            {
                var jobControl = _jobService.AddJob(job, dataService.WriteProgress, async (jobControl, cts) =>
                {
                    var result = await dataService.ExportAsync(parameters, catalogItemsMap, job.Id, cts.Token);
                    return result;
                });

                return this.Accepted(this.GetAcceptUrl(job.Id), job);
            }
            catch (ValidationException ex)
            {
                return this.UnprocessableEntity(ex.Message);
            }
        }

        /// <summary>
        /// Creates a new load packages job.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel the current operation.</param>
        [Authorize(Policy = Policies.RequireAdmin)]

        [HttpPost("load-packages")]
        public Task<ActionResult<Job>> LoadPackagesAsync(
            CancellationToken cancellationToken)
        {
            var job = new Job(Guid.NewGuid(), "load-packages", this.User.Identity.Name, default);
            var progress = new Progress<double>();

            var jobControl = _jobService.AddJob(job, progress, async (jobControl, cts) =>
            {
                await _appStateManager.LoadPackagesAsync(progress, cancellationToken);
                return null;
            });

            var response = (ActionResult<Job>)this.Accepted(this.GetAcceptUrl(job.Id), job);
            return Task.FromResult(response);
        }

        /// <summary>
        /// Gets a list of jobs.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public ActionResult<List<Job>> GetJobs()
        {
            var isAdmin = this.User.HasClaim(Claims.IS_ADMIN, "true");

            return _jobService
                .GetJobs()
                .Select(jobControl => jobControl.Job)
                .Where(job => job.Owner == this.User.Identity.Name || isAdmin)
                .ToList();
        }

        /// <summary>
        /// Gets the status of the specified job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("{jobId}/status")]
        public ActionResult<JobStatus> GetJobStatus(Guid jobId)
        {
            if (_jobService.TryGetJob(jobId, out var jobControl))
            {
                var isAdmin = this.User.HasClaim(Claims.IS_ADMIN, "true");

                if (jobControl.Job.Owner == this.User.Identity.Name || isAdmin)
                {
                    var status = new JobStatus(
                        Start: jobControl.Start,
                        Progress: jobControl.Progress,
                        Status: jobControl.Task.Status,
                        ExceptionMessage: jobControl.Task.Exception is not null
                            ? jobControl.Task.Exception.Message
                            : string.Empty,
                        Result: jobControl.Task.Status == TaskStatus.RanToCompletion && jobControl.Task.Result is not null
                            ? jobControl.Task.Result
                            : null);

                    return status;
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
        [HttpDelete("{jobId}")]
        public ActionResult DeleteJob(Guid jobId)
        {
            if (_jobService.TryGetJob(jobId, out var jobControl))
            {
                var isAdmin = this.User.HasClaim(Claims.IS_ADMIN, "true");

                if (jobControl.Job.Owner == this.User.Identity.Name || isAdmin)
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

        private string GetAcceptUrl(Guid jobId)
        {
            return $"{this.Request.Scheme}://{this.Request.Host}{this.Request.Path}/{jobId}/status";
        }

        #endregion
    }
}
