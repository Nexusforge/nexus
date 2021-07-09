using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record RepresentationPipeReader(
        CatalogItem CatalogItem,
        PipeReader DataReader);
}
