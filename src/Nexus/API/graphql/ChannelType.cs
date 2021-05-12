﻿using GraphQL.Types;
using static Nexus.Controllers.ProjectsController;

namespace Nexus.API
{
    public class ChannelType : ObjectGraphType<Channel>
    {
        public ChannelType()
        {
            this.Name = "Channel";

            this.Field(x => x.Id, type: typeof(IdGraphType))
                .Description("The channel ID.");

            this.Field(x => x.Name, type: typeof(IdGraphType))
                .Description("The channel name.");

            this.Field(x => x.Group)
                .Description("The channel group.");

            this.Field(x => x.Unit)
                .Description("The channel unit.");

            this.Field(x => x.Description)
                .Description("The channel description.");

            this.Field(x => x.SpecialInfo)
                .Description("Special info of the channel.");
        }
    }
}
