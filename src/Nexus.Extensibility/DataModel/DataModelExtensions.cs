using System.Collections.Generic;

namespace Nexus.DataModel
{
    public static class DataModelExtensions
    {
        public static ResourceCatalogBuilder WithDescription(this ResourceCatalogBuilder catalogBuilder, string description)
        {
            return catalogBuilder.WithProperty("Description", description);
        }

        public static ResourceCatalogBuilder WithShortDescription(this ResourceCatalogBuilder catalogBuilder, string shortDescription)
        {
            return catalogBuilder.WithProperty("ShortDescription", shortDescription);
        }

        public static ResourceCatalogBuilder WithLicense(this ResourceCatalogBuilder catalogBuilder, string license)
        {
            return catalogBuilder.WithProperty("License", license);
        }

        public static ResourceCatalogBuilder WithContact(this ResourceCatalogBuilder catalogBuilder, string contact)
        {
            return catalogBuilder.WithProperty("Contact", contact);
        }

        public static ResourceBuilder WithUnit(this ResourceBuilder resourceBuilder, string unit)
        {
            return resourceBuilder.WithProperty("Unit", unit);
        }

        public static ResourceBuilder WithDescription(this ResourceBuilder resourceBuilder, string description)
        {
            return resourceBuilder.WithProperty("Description", description);
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
                resourceBuilder.WithProperty($"Groups:{counter}", group);
                counter++;
            }

            return resourceBuilder;
        }
    }
}
