using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    /// <summary>
    /// Provides access to backend sources.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class BackendSourcesController : ControllerBase
    {
        // GET      /api/backendsources
        // PUT      /api/backendsources/{backendSourceId}
        // DELETE   /api/backendsources/{backendSourceId}

        #region Fields

        AppState _appState;
        AppStateManager _appStateManager;

        #endregion

        #region Constructors

        public BackendSourcesController(
            AppState appState,
            AppStateManager appStateManager)
        {
            _appState = appState;
            _appStateManager = appStateManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the list of backend sources.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public Task<IReadOnlyDictionary<Guid, BackendSource>>
            GetBackendSourcesAsync()
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            if (_appState.Project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                return Task.FromResult(userConfiguration.BackendSources);

            else
                return Task.FromResult((IReadOnlyDictionary<Guid, BackendSource>)new Dictionary<Guid, BackendSource>());
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="backendSourceId">The identifier of the backend source.</param>
        /// <param name="backendSource">The backend source to put.</param>
        [HttpPut("{backendSourceId}")]
        public Task
            PutBackendSourceAsync(
            Guid backendSourceId,
            [FromBody] BackendSource backendSource)
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            return _appStateManager.PutBackendSourceAsync(username, backendSourceId, backendSource);
        }

        /// <summary>
        /// Deletes a backend source.
        /// </summary>
        /// <param name="backendSourceId">The ID of the backend source.</param>
        [HttpDelete("{backendSourceId}")]
        public Task
            DeleteBackendSourceAsync(
            Guid backendSourceId)
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            return _appStateManager.DeleteBackendSourceAsync(username, backendSourceId);
        }

        #endregion
    }
}
