using System;

namespace Nexus.Extensibility
{
    /// <summary>
    /// An attribute to identify the extension.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
    public class ExtensionIdentificationAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionIdentificationAttribute"/>.
        /// </summary>
        /// <param name="id">The unique extension identifier.</param>
        /// <param name="name">The extension display name.</param>
        /// <param name="description">The extension description.</param>
        public ExtensionIdentificationAttribute(string id, string name, string description)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
        }

        /// <summary>
        /// Gets the extension identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the extension display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the extension description.
        /// </summary>
        public string Description { get; }
    }
}