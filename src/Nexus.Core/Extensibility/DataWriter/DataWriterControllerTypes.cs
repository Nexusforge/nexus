using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal record CatalogItemPipeReader(
        CatalogItem CatalogItem,
        PipeReader DataReader);
}
