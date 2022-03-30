﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to jobs.
    /// </summary>
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
        private Dictionary<CatalogContainer, IEnumerable<CatalogItemRequest>> catalogItemRequestMap;

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

            parameters = parameters with 
            { 
                Begin = parameters.Begin.ToUniversalTime(),
                End = parameters.End.ToUniversalTime()
            };

            var root = _appStateManager.AppState.CatalogState.Root;

            // translate resource paths to catalog item requests
            CatalogItemRequest[] catalogItemRequests;

            try
            {
                catalogItemRequests = await Task.WhenAll(parameters.ResourcePaths.Select(async resourcePath =>
                {
                    var catalogItemRequest = await root.TryFindAsync(resourcePath, cancellationToken);

                    if (catalogItemRequest is null)
                        throw new ValidationException($"Could not find resource path {resourcePath}.");

                    return catalogItemRequest;
                }));
            }
            catch (ValidationException ex)
            {
                return UnprocessableEntity(ex.Message);
            }

            // check that there is anything to export
            if (!catalogItemRequests.Any())
                return BadRequest("The list of resource paths is empty.");

            // build up catalog items map and authorize
            try
            {
                foreach (var group in catalogItemRequests.GroupBy(current => current.Container.Id))
                {
                    var catalogContainer = group.First().Container;

                    if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.Metadata, HttpContext.User))
                        throw new UnauthorizedAccessException($"The current user is not permitted to access catalog {catalogContainer.Id}.");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }

            //
            var username = User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            var job = new Job(Guid.NewGuid(), "export", username, parameters);
            var dataService = _serviceProvider.GetRequiredService<IDataService>();

            try
            {
                var jobControl = _jobService.AddJob(job, dataService.WriteProgress, async (jobControl, cts) =>
                {
                    var result = await dataService.ExportAsync(parameters, catalogItemRequests, job.Id, cts.Token);
                    return result;
                });

                return Accepted(GetAcceptUrl(job.Id), job);
            }
            catch (ValidationException ex)
            {
                return UnprocessableEntity(ex.Message);
            }
        }

        /// <summary>
        /// Creates a new load packages job.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel the current operation.</param>
        [Authorize(Policy = Policies.RequireAdmin)]

        [HttpPost("load-packages")]
        public ActionResult<Job> LoadPackages(
            CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            var job = new Job(Guid.NewGuid(), "load-packages", username, default);
            var progress = new Progress<double>();

            var jobControl = _jobService.AddJob(job, progress, async (jobControl, cts) =>
            {
                await _appStateManager.LoadPackagesAsync(progress, cancellationToken);
                return null;
            });

            var response = (ActionResult<Job>)Accepted(GetAcceptUrl(job.Id), job);
            return response;
        }

        /// <summary>
        /// Gets a list of jobs.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public ActionResult<List<Job>> GetJobs()
        {
            var isAdmin = User.HasClaim(NexusClaims.IS_ADMIN, "true");
            var username = User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            var result = _jobService
                .GetJobs()
                .Select(jobControl => jobControl.Job)
                .Where(job => job.Owner == username || isAdmin)
                .ToList();

            return result;
        }

        /// <summary>
        /// Gets the status of the specified job.
        /// </summary>
        /// <param name="jobId"></param>
        /// <returns></returns>
        [HttpGet("{jobId}/status")]
        public async Task<ActionResult<JobStatus>> GetJobStatusAsync(Guid jobId)
        {
            if (_jobService.TryGetJob(jobId, out var jobControl))
            {
                var isAdmin = User.HasClaim(NexusClaims.IS_ADMIN, "true");
                var username = User.Identity?.Name;

                if (username is null)
                    throw new Exception("This should never happen.");

                if (jobControl.Job.Owner == username || isAdmin)
                {
                    var status = new JobStatus(
                        Start: jobControl.Start,
                        Progress: jobControl.Progress,
                        Status: jobControl.Task.Status,
                        ExceptionMessage: jobControl.Task.Exception is not null
                            ? jobControl.Task.Exception.Message
                            : string.Empty,
                        Result: jobControl.Task.Status == TaskStatus.RanToCompletion && (await jobControl.Task) is not null
                            ? await jobControl.Task
                            : null);

                    return status;
                }
                else
                {
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to access the status of job {jobControl.Job.Id}.");
                }
            }
            else
            {
                return NotFound(jobId);
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
                var isAdmin = User.HasClaim(NexusClaims.IS_ADMIN, "true");
                var username = User.Identity?.Name;

                if (username is null)
                    throw new Exception("This should never happen.");

                if (jobControl.Job.Owner == username || isAdmin)
                {
                    jobControl.CancellationTokenSource.Cancel();
                    return Accepted();
                }

                else
                {
                    return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to cancel the job {jobControl.Job.Id}.");
                }
            }

            else
            {
                return NotFound(jobId);
            }
        }

        #endregion

        #region Methods

        private string GetAcceptUrl(Guid jobId)
        {
            return $"{Request.Scheme}://{Request.Host}{Request.Path}/{jobId}/status";
        }

        #endregion
    }
}
