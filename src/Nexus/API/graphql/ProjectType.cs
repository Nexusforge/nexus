using GraphQL;
using GraphQL.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using Channel = Nexus.Controllers.CatalogsController.Channel;
using Catalog = Nexus.DataModel.Catalog;

namespace Nexus.API
{
    public class CatalogType : ObjectGraphType<(Catalog Catalog, CatalogMeta Meta)>
    {
        public CatalogType()
        {
            this.Name = "Catalog";

            this.Field(x => x.Catalog.Id, type: typeof(IdGraphType))
                .Description("The catalog ID.");

            this.Field<ChannelType>(
                "Channel",
                arguments: new QueryArguments(
                    new QueryArgument<IdGraphType> { Name = "id", Description = "The ID of the channel." }),
                resolve: context =>
            {
                var id = context.GetArgument<Guid>("id");
                var catalog = context.Source.Catalog;
                var catalogMeta = context.Source.Meta;

                var channel = catalog.Channels.First(current => current.Id == id);
                var channelMeta = catalogMeta.Channels.First(current => current.Id == id);

#warning taken from CatalogsController, unify this
                var channel2 = new Channel()
                {
                    Id = channel.Id,
                    Name = channel.Name,
                    Group = channel.Group,
                    Unit = !string.IsNullOrWhiteSpace(channelMeta.Unit)
                            ? channelMeta.Unit
                            : channel.Unit,
                    Description = !string.IsNullOrWhiteSpace(channelMeta.Description)
                            ? channelMeta.Description
                            : channel.Description,
                    SpecialInfo = channelMeta.SpecialInfo
                };

                return channel2;
            });
            //.Description("A list of channels defined in the catalog.");

            this.Field<ListGraphType<ChannelType>>("Channels", resolve: context =>
            {
                var result = new List<Channel>();
                var catalog = context.Source.Catalog;
                var catalogMeta = context.Source.Meta;

                foreach (var channel in catalog.Channels)
                {
                    var channelMeta = catalogMeta.Channels.First(current => current.Id == channel.Id);

#warning taken from CatalogsController, unify this
                    var channel2 = new Channel()
                    {
                        Id = channel.Id,
                        Name = channel.Name,
                        Group = channel.Group,
                        Unit = !string.IsNullOrWhiteSpace(channelMeta.Unit)
                                ? channelMeta.Unit
                                : channel.Unit,
                        Description = channelMeta.Description,
                        SpecialInfo = channelMeta.SpecialInfo
                    };

                    result.Add(channel2);
                }

                return result;
            });
              //.Description("A list of channels defined in the catalog.");

            this.Field(x => x.Meta.ShortDescription)
                .Description("A short catalog description.");

            this.Field(x => x.Meta.LongDescription)
                .Description("A long catalog description.");

            this.Field(x => x.Meta.Contact)
                .Description("A catalog contact.");
        }
    }
}
