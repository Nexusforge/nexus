using GraphQL.Types;
using static Nexus.Controllers.CatalogsController;

namespace Nexus.API
{
    public class ResourceType : ObjectGraphType<Resource>
    {
        public ResourceType()
        {
            this.Name = "Resource";

            this.Field(x => x.Id, type: typeof(IdGraphType))
                .Description("The resource ID.");

            this.Field(x => x.Name, type: typeof(IdGraphType))
                .Description("The resource name.");

            this.Field(x => x.Group)
                .Description("The resource group.");

            this.Field(x => x.Unit)
                .Description("The resource unit.");

            this.Field(x => x.Description)
                .Description("The resource description.");

            this.Field(x => x.SpecialInfo)
                .Description("Special info of the resource.");
        }
    }
}
