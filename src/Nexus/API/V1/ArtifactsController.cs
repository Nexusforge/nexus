using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    /// <summary>
    /// Provides access to artifacts.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class ArtifactsController : ControllerBase
    {
        // GET      /api/artifacts/{artifactId}

        #region Fields

        private IDatabaseManager _databaseManager;

        #endregion

        #region Constructors

        internal ArtifactsController(
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
        internal Task<ActionResult>
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
