using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    [Authorize(Policy = Policies.RequireAdmin)]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class AppState2Controller : ControllerBase
    {
        // POST     /api/appstate/reload-packages

        #region Fields

        private AppStateManager _appStateManager;

        #endregion

        #region Constructors

        public AppState2Controller(
            AppStateManager appStateManager)
        {
            _appStateManager = appStateManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Reloads all packages.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the current operation.</param>
        [HttpPost("{packageReferenceId}")]
        public Task
            ReloadPackagesAsync(
            CancellationToken cancellationToken)
        {
            return _appStateManager.ReloadPackagesAsync(cancellationToken);
        }

        #endregion
    }
}