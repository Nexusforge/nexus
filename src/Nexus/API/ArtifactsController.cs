﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Services;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to artifacts.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class ArtifactsController : ControllerBase
    {
        // GET      /api/artifacts/{artifactId}

        #region Fields

        public IDatabaseManager _databaseManager;

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
        public ActionResult
            Download(
                string artifactId)
        {
            if (_databaseManager.TryReadArtifact(artifactId, out var artifactStream))
            {
                Response.Headers.ContentLength = artifactStream.Length;
                return File(artifactStream, "application/octet-stream", artifactId);
            }

            else
            {
                return NotFound($"Could not find artifact {artifactId}.");
            }
        }

        #endregion
    }
}
