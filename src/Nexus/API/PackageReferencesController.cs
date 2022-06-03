using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to package references.
    /// </summary>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class PackageReferencesController : ControllerBase
    {
        // GET      /api/packagereferences
        // PUT      /api/packagereferences
        // DELETE   /api/packagereferences/{packageReferenceId}
        // GET      /api/packagereferences/{packageReferenceId}/versions

        #region Fields

        private AppState _appState;
        private AppStateManager _appStateManager;
        private IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        public PackageReferencesController(
            AppState appState,
            AppStateManager appStateManager,
            IExtensionHive extensionHive)
        {
            _appState = appState;
            _appStateManager = appStateManager;
            _extensionHive = extensionHive;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the list of package references.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<PackageReference> Get()
        {
            return _appState.Project.PackageReferences.Values;
        }

        /// <summary>
        /// Puts a package reference.
        /// </summary>
        /// <param name="packageReference">The package reference to set.</param>
        [HttpPut]
        public Task SetAsync(
            [FromBody] PackageReference packageReference)
        {
            return _appStateManager.PutPackageReferenceAsync(packageReference);
        }

        /// <summary>
        /// Deletes a package reference.
        /// </summary>
        /// <param name="packageReferenceId">The ID of the package reference.</param>
        [HttpDelete("{packageReferenceId}")]
        public Task DeleteAsync(
            Guid packageReferenceId)
        {
            return _appStateManager.DeletePackageReferenceAsync(packageReferenceId);
        }

        /// <summary>
        /// Gets package versions.
        /// </summary>
        /// <param name="packageReferenceId">The ID of the package reference.</param>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpGet("{packageReferenceId}/versions")]
        public async Task<ActionResult<string[]>> GetVersionsAsync(
            Guid packageReferenceId,
            CancellationToken cancellationToken)
        {
            var project = _appState.Project;

            if (!project.PackageReferences.TryGetValue(packageReferenceId, out var packageReference))
                return NotFound($"Unable to find package reference with ID {packageReferenceId}.");

            var result = await _extensionHive
                .GetVersionsAsync(packageReference, cancellationToken);

            return result;
        }

        #endregion
    }
}