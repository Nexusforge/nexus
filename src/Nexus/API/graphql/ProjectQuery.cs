using GraphQL;
using GraphQL.Types;
using Nexus.Services;
using System.Linq;

namespace Nexus.API
{
    public class CatalogQuery : ObjectGraphType
    {
        public CatalogQuery(IDatabaseManager databaseManager)
        {
            this.Field<CatalogType>(
                "Catalog",
                arguments: new QueryArguments(
                    new QueryArgument<IdGraphType> { Name = "id", Description = "The ID of the catalog." }),
                resolve: context =>
                {
                    var id = context.GetArgument<string>("id");
                    var catalogContainer = databaseManager.Database.CatalogContainers
                        .FirstOrDefault(catalogContainer => catalogContainer.Id == id);

                    if (catalogContainer != null)
                        return (catalogContainer.Catalog, catalogContainer.CatalogMeta);
                    else
                        return null;
                });

            this.Field<CatalogType>(
                "Catalogs",
                resolve: context =>
                {
                    return databaseManager.Database.CatalogContainers
                        .Select(catalogContainer => (catalogContainer.Catalog, catalogContainer.CatalogMeta));
                });
        }
    }
}
