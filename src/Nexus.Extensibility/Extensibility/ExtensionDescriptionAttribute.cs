using System;

namespace Nexus.Extensibility
{
    /// <summary>
    /// An attribute to identify the extension.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
    public class ExtensionDescriptionAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionDescriptionAttribute"/>.
        /// </summary>
        /// <param name="description">The extension description.</param>
        public ExtensionDescriptionAttribute(string description)
        {
            this.Description = description;
        }

        /// <summary>
        /// Gets the extension description.
        /// </summary>
        public string Description { get; }
    }
}