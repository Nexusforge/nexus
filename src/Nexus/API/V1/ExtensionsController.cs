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
    internal class ExtensionsController : ControllerBase
    {
        // GET      /api/extensions/sources
        // GET      /api/extensions/writers

        #region Fields

        IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        public ExtensionsController(
            IExtensionHive extensionHive)
        {
            _extensionHive = extensionHive;
        }

        #endregion

        /// <summary>
        /// Gets the list of sources.
        /// </summary>
        [HttpGet("sources")]
        public Task<ExtensionDescription[]> GetSourcesAsync()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets the list of writers.
        /// </summary>
        [HttpGet("writers")]
        public Task<ExtensionDescription[]> GetWritersAsync()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataWriter>());
            return Task.FromResult(result);
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
