using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using System.Text.Json;

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
        private GeneralOptions _generalOptions;

        #endregion

        #region Constructors

        public SystemController(
            AppState appState,
            AppStateManager appStateManager,
            IOptions<GeneralOptions> generalOptions)
        {
            _generalOptions = generalOptions.Value;
            _appState = appState;
            _appStateManager = appStateManager;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the configured help link.
        /// </summary>
        [HttpGet("help-link")]
        public string? GetHelpLink()
        {
            return _generalOptions.HelpLink;
        }

        /// <summary>
        /// Gets the system configuration.
        /// </summary>
        [HttpGet("configuration")]
        public JsonElement? GetConfiguration()
        {
            return _appState.Project.SystemConfiguration;
        }

        /// <summary>
        /// Sets the system configuration.
        /// </summary>
        [HttpPut("configuration")]
        public Task SetConfigurationAsync(JsonElement? configuration)
        {
            return _appStateManager.PutSystemConfigurationAsync(configuration);
        }

        #endregion
    }
}
