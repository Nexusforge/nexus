using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record CatalogItemPipeReader(
        CatalogItem CatalogItem,
        PipeReader DataReader);
}
