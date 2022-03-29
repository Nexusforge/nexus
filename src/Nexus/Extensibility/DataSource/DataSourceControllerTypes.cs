using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal record CatalogItemPipeWriter(
        CatalogItem CatalogItem, 
        PipeWriter DataWriter);

    internal record DataReadingGroup(
        IDataSourceController Controller,
        CatalogItemPipeWriter[] CatalogItemPipeWriters);
}
