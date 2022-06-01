using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using System.Diagnostics.CodeAnalysis;
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
    internal class SourcesController : ControllerBase
    {
        // GET      /api/sources/descriptions
        // GET      /api/sources/registrations
        // PUT      /api/sources/registrations/{registrationId}
        // DELETE   /api/sources/registrations/{registrationId}

        #region Fields

        private AppState _appState;
        private AppStateManager _appStateManager;
        private IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        public SourcesController(
            AppState appState,
            AppStateManager appStateManager,
            IExtensionHive extensionHive)
        {
            _appState = appState;
            _appStateManager = appStateManager;
            _extensionHive = extensionHive;
        }

        #endregion

        /// <summary>
        /// Gets the list of sources.
        /// </summary>
        [HttpGet("descriptions")]
        public List<ExtensionDescription> GetDescriptions()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
            return result;
        }

        /// <summary>
        /// Gets the list of backend sources.
        /// </summary>
        /// <param name="username">The optional username. If not specified, the name of the current user will be used.</param>
        /// <returns></returns>
        [HttpGet("registrations")]
        public ActionResult<IReadOnlyDictionary<Guid, DataSourceRegistration>>
            GetRegistrations([FromQuery] string? username = default)
        {
            if (TryAuthenticate(username, out var actualUsername, out var response))
            {
                if (_appState.Project.UserConfigurations.TryGetValue(actualUsername, out var userConfiguration))
                    return Ok(userConfiguration.DataSourceRegistrations);

                else
                    return new Dictionary<Guid, DataSourceRegistration>();
            }

            else
            {
                return response;
            }
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="registrationId">The identifier of the registration.</param>
        /// <param name="registration">The registration to put.</param>
        /// <param name="username">The optional username. If not specified, the name of the current user will be used.</param>
        [HttpPut("registrations/{registrationId}")]
        public async Task<ActionResult>
            SetRegistrationAsync(
            Guid registrationId,
            [FromBody] DataSourceRegistration registration,
            [FromQuery] string? username = default)
        {
            if (TryAuthenticate(username, out var actualUsername, out var response))
            {
                await _appStateManager.PutDataSourceRegistrationAsync(actualUsername, registrationId, registration);
                return Ok();
            }

            else
            {
                return response;
            }
        }

        /// <summary>
        /// Deletes a backend source.
        /// </summary>
        /// <param name="registrationId">The identifier of the registration.</param>
        /// <param name="username">The optional username. If not specified, the name of the current user will be used.</param>
        [HttpDelete("registrations/{registrationId}")]
        public async Task<ActionResult>
            DeleteRegistrationAsync(
            Guid registrationId,
            [FromQuery] string? username = default)
        {
            if (TryAuthenticate(username, out var actualUsername, out var response))
            {
                await _appStateManager.DeleteDataSourceRegistrationAsync(actualUsername, registrationId);
                return Ok();
            }

            else
            {
                return response;
            }
        }

        private List<ExtensionDescription> GetExtensionDescriptions(IEnumerable<Type> extensions)
        {
            return extensions.Select(type =>
            {
                var attribute = type.GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                var description = attribute is null
                    ? null
                    : attribute.Description;

                return new ExtensionDescription(type.FullName ?? throw new Exception("fullname is null"), description, default);
            })
            .ToList();
        }

        private bool TryAuthenticate(
            string? requestedUsername,
            out string username,
            [NotNullWhen(returnValue: false)] out ActionResult? response)
        {
            var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
            var currentUsername = User.Identity?.Name!;

            if (!(isAdmin || requestedUsername is null || requestedUsername == currentUsername))
                response = StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to get source registrations of user {requestedUsername}.");

            else
                response = null;

            username = requestedUsername is null
                ? currentUsername
                : requestedUsername;
            
            return response is null;
        }
    }
}
