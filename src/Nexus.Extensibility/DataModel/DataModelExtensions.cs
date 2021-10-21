using System.Collections.Generic;

namespace Nexus.DataModel
{
    public static class DataModelExtensions
    {
        internal const string Description = "Description";
        internal const string ShortDescription = "ShortDescription";
        internal const string Warning = "Warning";
        internal const string Contact = "Contact";
        internal const string Unit = "Unit";
        internal const string Groups = "Groups";

        public static ResourceCatalogBuilder WithDescription(this ResourceCatalogBuilder catalogBuilder, string description)
        {
            return catalogBuilder.WithProperty(Description, description);
        }

        public static ResourceCatalogBuilder WithShortDescription(this ResourceCatalogBuilder catalogBuilder, string shortDescription)
        {
            return catalogBuilder.WithProperty(ShortDescription, shortDescription);
        }

        public static ResourceCatalogBuilder WithContact(this ResourceCatalogBuilder catalogBuilder, string contact)
        {
            return catalogBuilder.WithProperty(Contact, contact);
        }

        public static ResourceBuilder WithUnit(this ResourceBuilder resourceBuilder, string unit)
        {
            return resourceBuilder.WithProperty(Unit, unit);
        }

        public static ResourceBuilder WithDescription(this ResourceBuilder resourceBuilder, string description)
        {
            return resourceBuilder.WithProperty(Description, description);
        }

        public static ResourceBuilder WithWarning(this ResourceBuilder resourceBuilder, string warning)
        {
            return resourceBuilder.WithProperty(Warning, warning);
        }

        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, params string[] groups)
        {
            return resourceBuilder.WithGroups((IEnumerable<string>)groups);
        }

        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, IEnumerable<string> groups)
        {
            var counter = 0;

            foreach (var group in groups)
            {
                resourceBuilder.WithProperty($"{Groups}:{counter}", group);
                counter++;
            }

            return resourceBuilder;
        }
    }
}
