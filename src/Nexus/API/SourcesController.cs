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
        public ExtensionDescription[] GetDescriptions()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
            return result;
        }

        /// <summary>
        /// Gets the list of backend sources.
        /// </summary>
        /// <returns></returns>
        [HttpGet("registrations")]
        public IReadOnlyDictionary<Guid, DataSourceRegistration>
            GetRegistrations()
        {
            var username = User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            if (_appState.Project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                return userConfiguration.DataSourceRegistrations;

            else
                return new Dictionary<Guid, DataSourceRegistration>();
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="registrationId">The identifier of the registration.</param>
        /// <param name="registration">The registration to put.</param>
        [HttpPut("registrations/{registrationId}")]
        public Task
            PutRegistrationAsync(
            Guid registrationId,
            [FromBody] DataSourceRegistration registration)
        {
            var username = User.Identity?.Name!;
            return _appStateManager.PutDataSourceRegistrationAsync(username, registrationId, registration);
        }

        /// <summary>
        /// Deletes a backend source.
        /// </summary>
        /// <param name="registrationId">The identifier of the registration.</param>
        [HttpDelete("registrations/{registrationId}")]
        public Task
            DeleteRegistrationAsync(
            Guid registrationId)
        {
            var username = User.Identity?.Name!;
            return _appStateManager.DeleteDataSourceRegistrationAsync(username, registrationId);
        }

        private ExtensionDescription[] GetExtensionDescriptions(IEnumerable<Type> extensions)
        {
            return extensions.Select(type =>
            {
                var attribute = type.GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

                var description = attribute is null
                    ? null
                    : attribute.Description;

                return new ExtensionDescription(type.FullName ?? throw new Exception("fullname is null"), description);
            })
            .ToArray();
        }
    }
}
