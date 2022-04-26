using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using System.Reflection;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to extensions.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class WritersController : ControllerBase
    {
        // GET      /api/writers/descriptions

        #region Fields

        private AppState _appState;

        #endregion

        #region Constructors

        public WritersController(
            AppState appState)
        {
            _appState = appState;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the list of writers.
        /// </summary>
        [HttpGet("descriptions")]
        public List<ExtensionDescription> GetDescriptions()
        {
            return _appState.DataWriterDescriptions;
        }

        #endregion
    }
}
