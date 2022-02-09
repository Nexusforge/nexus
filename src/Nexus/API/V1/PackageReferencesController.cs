using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    /// <summary>
    /// Provides access to package references.
    /// </summary>
    [Authorize(Policy = Policies.RequireAdmin)]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class PackageReferencesController : ControllerBase
    {
        // GET      /api/packagereferences
        // PUT      /api/packagereferences/{packageReferenceId}
        // DELETE   /api/packagereferences/{packageReferenceId}
        // GET      /api/packagereferences/{packageReferenceId}/versions

        #region Fields

        private AppState _appState;
        private AppStateManager _appStateManager;
        private IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        internal PackageReferencesController(
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
        public Task<IReadOnlyDictionary<Guid, PackageReference>>
            GetPackageReferencesAsync()
        {
            return Task.FromResult(_appState.Project.PackageReferences);
        }

        /// <summary>
        /// Puts a package reference.
        /// </summary>
        /// <param name="packageReferenceId">The identifier of the package reference.</param>
        /// <param name="packageReference">The package reference to put.</param>
        [HttpPut("{packageReferenceId}")]
        public Task
            PutPackageReferencesAsync(
            Guid packageReferenceId,
            [FromBody] PackageReference packageReference)
        {
            return _appStateManager.PutPackageReferenceAsync(packageReferenceId, packageReference);
        }

        /// <summary>
        /// Deletes a package reference.
        /// </summary>
        /// <param name="packageReferenceId">The ID of the package reference.</param>
        [HttpDelete("{packageReferenceId}")]
        public Task
            DeletePackageReferencesAsync(
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
        public async Task<ActionResult<string[]>>
            GetPackageVersionsAsync(
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