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
        // PUT      /api/sources/registrations
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
        /// Gets the list of source descriptions.
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
        public ActionResult<IEnumerable<DataSourceRegistration>> GetRegistrations(
            [FromQuery] string? username = default)
        {
            if (TryAuthenticate(username, out var actualUsername, out var response))
            {
                if (_appState.Project.UserConfigurations.TryGetValue(actualUsername, out var userConfiguration))
                    return Ok(userConfiguration.DataSourceRegistrations.Values);

                else
                    return Ok(Enumerable.Empty<DataSourceRegistration>());
            }

            else
            {
                return response;
            }
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="registration">The registration to set.</param>
        /// <param name="username">The optional username. If not specified, the name of the current user will be used.</param>
        [HttpPut("registrations")]
        public async Task<ActionResult> SetRegistrationAsync(
            [FromBody] DataSourceRegistration registration,
            [FromQuery] string? username = default)
        {
            if (TryAuthenticate(username, out var actualUsername, out var response))
            {
                await _appStateManager.PutDataSourceRegistrationAsync(actualUsername, registration);
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
        public async Task<ActionResult> DeleteRegistrationAsync(
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

        private List<ExtensionDescription> GetExtensionDescriptions(
            IEnumerable<Type> extensions)
        {
            return extensions.Select(type =>
            {
                var attribute = type.GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                if (attribute is null)
                    return new ExtensionDescription(type.FullName!, default, default, default, default);

                else
                    return new ExtensionDescription(type.FullName!, attribute.Description, attribute.ProjectUrl, attribute.RepositoryUrl, default);
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
