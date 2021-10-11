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
            return catalogBuilder.WithProperty("Nexus:ShortDescription", shortDescription);
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
            return resourceBuilder.WithGroups(groups);
        }

        public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, IEnumerable<string> groups)
        {
            var counter = 0;

            foreach (var group in groups)
            {
                resourceBuilder.WithProperty($"Nexus:Groups:{counter}", group);
                counter++;
            }

            return resourceBuilder;
        }
    }
}
