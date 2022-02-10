using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using System.Reflection;

namespace Nexus.Controllers.V1
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
        public Task<ExtensionDescription[]> GetSourceDescriptionsAsync()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets the list of backend sources.
        /// </summary>
        /// <returns></returns>
        [HttpGet("registrations")]
        public Task<IReadOnlyDictionary<Guid, DataSourceRegistration>>
            GetSourceRegistrationsAsync()
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            if (_appState.Project.UserConfigurations.TryGetValue(username, out var userConfiguration))
                return Task.FromResult(userConfiguration.DataSourceRegistrations);

            else
                return Task.FromResult((IReadOnlyDictionary<Guid, DataSourceRegistration>)new Dictionary<Guid, DataSourceRegistration>());
        }

        /// <summary>
        /// Puts a backend source.
        /// </summary>
        /// <param name="registrationId">The identifier of the backend source.</param>
        /// <param name="registration">The backend source to put.</param>
        [HttpPut("registrations/{registrationId}")]
        public Task
            PutSourceRegistrationAsync(
            Guid registrationId,
            [FromBody] DataSourceRegistration registration)
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            return _appStateManager.PutDataSourceRegistrationAsync(username, registrationId, registration);
        }

        /// <summary>
        /// Deletes a backend source.
        /// </summary>
        /// <param name="registrationId">The ID of the backend source.</param>
        [HttpDelete("registrations/{registrationId}")]
        public Task
            DeleteSourceRegistrationAsync(
            Guid registrationId)
        {
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

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
