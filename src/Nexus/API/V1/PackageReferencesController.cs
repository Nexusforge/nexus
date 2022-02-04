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
        // POST     /api/packagereferences/reload

        #region Fields

        private AppState _appState;
        private AppStateController _appStateController;

        #endregion

        #region Constructors

        public PackageReferencesController(
            AppState appState,
            AppStateController appStateController)
        {
            _appState = appState;
            _appStateController = appStateController;
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
            return _appStateController.PutPackageReferenceAsync(packageReferenceId, request.PackageReference);

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
            return _appStateController.DeletePackageReferenceAsync(packageReferenceId);

        }

#error Better "reload packages" instead of "reload packagereferences"?
#error Is POST ok?
#error Generate client
#error Add controller for backend sources

        /// <summary>
        /// Reloads all packages.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpPost("{packageReferenceId}")]
        public Task
            ReloadPackagesAsync(
            CancellationToken cancellationToken)
        {
            return _appStateController.ReloadPackagesAsync(cancellationToken);
        }

        #endregion
    }
}