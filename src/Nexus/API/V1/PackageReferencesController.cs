using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Models;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    [Authorize(Policy = Policies.RequireAdmin)]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class PackageReferencesController : ControllerBase
    {
        // GET      /api/packagereferences
        // PUT      /api/packagereferences/{packageReferenceId}
        // DELETE   /api/packagereferences/{packageReferenceId}

        #region Fields

        private AppState _appState;
        private AppStateManager _appStateManager;

        #endregion

        #region Constructors

        public PackageReferencesController(
            AppState appState,
            AppStateManager appStateManager)
        {
            _appState = appState;
            _appStateManager = appStateManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the list of package references.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public Task<IReadOnlyDictionary<Guid, PackageReference>>
            GetPackagesAsync()
        {
            return Task.FromResult(_appState.Project.PackageReferences);
        }

        /// <summary>
        /// Puts a package reference.
        /// </summary>
        /// <param name="packageReferenceId">The ID of the package reference.</param>
        /// <param name="request"></param>
        [HttpPut("{packageReferenceId}")]
        public Task
            GetPackagesAsync(
            Guid packageReferenceId,
            [FromBody] AddPackageReferenceRequest request)
        {
            return _appStateManager.PutPackageReferenceAsync(packageReferenceId, request.PackageReference);

        }

        /// <summary>
        /// Deletes a package reference.
        /// </summary>
        /// <param name="packageReferenceId">The ID of the package reference.</param>
        [HttpDelete("{packageReferenceId}")]
        public Task
            GetPackagesAsync(
            Guid packageReferenceId)
        {
            return _appStateManager.DeletePackageReferenceAsync(packageReferenceId);

        }

        #endregion
    }
}