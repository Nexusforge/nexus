using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Models;

namespace Nexus.Controllers.V1
{
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


        #endregion

        #region Constructors

        public BackendSourcesController(
            )
        {
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
#error Implement this! CatalogManager loads UserConfig ... but it should be loaded earlier and be part of the AppState!
            return Task.FromResult(_appState.Project.BackendSources);
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="backendSourceId">The ID of the backend source.</param>
        /// <param name="request"></param>
        [HttpPut("{backendSourceId}")]
        public Task
            PutBackendSourceAsync(
            Guid backendSourceId,
            [FromBody] PutBackendSourceRequest request)
        {
            return _appStateManager.PutBackendSourceAsync(backendSourceId, request.BackendSource);

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
            return _appStateManager.DeleteBackendSourceAsync(backendSourceId);
        }

        #endregion
    }
}
