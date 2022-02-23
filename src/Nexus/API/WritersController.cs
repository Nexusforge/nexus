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

        private IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        public WritersController(
            IExtensionHive extensionHive)
        {
            _extensionHive = extensionHive;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the list of writers.
        /// </summary>
        [HttpGet("descriptions")]
        public ExtensionDescription[] GetWriterDescriptions()
        {
            var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataWriter>());
            return result;
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

        #endregion
    }
}
