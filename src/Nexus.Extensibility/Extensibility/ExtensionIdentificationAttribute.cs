using System;

namespace Nexus.Extensibility
{
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
    public class ExtensionIdentificationAttribute : Attribute
    {
        public ExtensionIdentificationAttribute(string id, string name, string description)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
        }

        public string Id { get; }

        public string Name { get; }

        public string Description { get; }
    }
}