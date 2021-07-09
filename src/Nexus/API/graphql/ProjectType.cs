using GraphQL;
using GraphQL.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using Resource = Nexus.Controllers.CatalogsController.Resource;
using ResourceCatalog = Nexus.DataModel.ResourceCatalog;

namespace Nexus.API
{
    public class CatalogType : ObjectGraphType<(ResourceCatalog Catalog, CatalogMeta Meta)>
    {
        public CatalogType()
        {
            this.Name = "Catalog";

            this.Field(x => x.Catalog.Id, type: typeof(IdGraphType))
                .Description("The catalog ID.");

            this.Field<ResourceType>(
                "Resource",
                arguments: new QueryArguments(
                    new QueryArgument<IdGraphType> { Name = "id", Description = "The ID of the resource." }),
                resolve: context =>
            {
                var id = context.GetArgument<Guid>("id");
                var catalog = context.Source.Catalog;
                var catalogMeta = context.Source.Meta;

                var resource = catalog.Resources.First(current => current.Id == id);
                var resourceMeta = catalogMeta.Resources.First(current => current.Id == id);

#warning taken from CatalogsController, unify this
                var resource2 = new Resource()
                {
                    Id = resource.Id,
                    Name = resource.Name,
                    Group = resource.Group,
                    Unit = !string.IsNullOrWhiteSpace(resourceMeta.Unit)
                            ? resourceMeta.Unit
                            : resource.Unit,
                    Description = !string.IsNullOrWhiteSpace(resourceMeta.Description)
                            ? resourceMeta.Description
                            : resource.Description,
                    SpecialInfo = resourceMeta.SpecialInfo
                };

                return resource2;
            });
            //.Description("A list of resources defined in the catalog.");

            this.Field<ListGraphType<ResourceType>>("Resources", resolve: context =>
            {
                var result = new List<Resource>();
                var catalog = context.Source.Catalog;
                var catalogMeta = context.Source.Meta;

                foreach (var resource in catalog.Resources)
                {
                    var resourceMeta = catalogMeta.Resources.First(current => current.Id == resource.Id);

#warning taken from CatalogsController, unify this
                    var resource2 = new Resource()
                    {
                        Id = resource.Id,
                        Name = resource.Name,
                        Group = resource.Group,
                        Unit = !string.IsNullOrWhiteSpace(resourceMeta.Unit)
                                ? resourceMeta.Unit
                                : resource.Unit,
                        Description = resourceMeta.Description,
                        SpecialInfo = resourceMeta.SpecialInfo
                    };

                    result.Add(resource2);
                }

                return result;
            });
              //.Description("A list of resources defined in the catalog.");

            this.Field(x => x.Meta.ShortDescription)
                .Description("A short catalog description.");

            this.Field(x => x.Meta.LongDescription)
                .Description("A long catalog description.");

            this.Field(x => x.Meta.Contact)
                .Description("A catalog contact.");
        }
    }
}
