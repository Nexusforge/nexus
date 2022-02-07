using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class ArtifactsController : ControllerBase
    {
        // GET      /api/artifacts/{artifactId}

        #region Fields

        private IDatabaseManager _databaseManager;

        #endregion

        #region Constructors

        public ArtifactsController(
            IDatabaseManager databaseManager)
        {
            _databaseManager = databaseManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the specified artifact.
        /// </summary>
        /// <param name="artifactId">The artifact identifier.</param>
        [HttpGet("{artifactId}")]
        public Task<ActionResult>
            DownloadArtifactAsync(
                string artifactId)
        {
            if (_databaseManager.TryReadArtifact(artifactId, out var artifactStream))
            {
                this.Response.Headers.ContentLength = artifactStream.Length;
                return Task.FromResult((ActionResult)
                    this.File(artifactStream, "application/octet-stream", artifactId));
            }
            else
            {
                return Task.FromResult((ActionResult)
                    this.NotFound($"Could not find artifact {artifactId}."));
            }
        }

        #endregion
    }
}
