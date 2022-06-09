using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to the system.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class SystemController : ControllerBase
    {
        // GET      /api/system/configuration
        // PUT      /api/system/configuration

        #region Fields

        private AppState _appState;
        private AppStateManager _appStateManager;

        #endregion

        #region Constructors

        public SystemController(
            AppState appState,
            AppStateManager appStateManager)
        {
            _appState = appState;
            _appStateManager = appStateManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the system configuration.
        /// </summary>
        [HttpGet("configuration")]
        public IReadOnlyDictionary<string, string> GetConfiguration()
        {
            return _appState.Project.SystemConfiguration;
        }

        /// <summary>
        /// Sets the system configuration.
        /// </summary>
        [HttpPut("configuration")]
        public Task SetConfigurationAsync(Dictionary<string, string> configuration)
        {
            return _appStateManager.PutSystemConfigurationAsync(configuration);
        }

        #endregion
    }
}
