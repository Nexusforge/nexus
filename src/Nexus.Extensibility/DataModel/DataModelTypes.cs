namespace Nexus.DataModel
{
    public enum NexusDataType
    {
        UINT8 = 0x108,
        INT8 = 0x208,
        UINT16 = 0x110,
        INT16 = 0x210,
        UINT32 = 0x120,
        INT32 = 0x220,
        UINT64 = 0x140,
        INT64 = 0x240,
        FLOAT32 = 0x320,
        FLOAT64 = 0x340
    }

    public enum MergeMode
    {
        ExclusiveOr,
        NewWins
    }

    public record CatalogItem(ResourceCatalog Catalog, Resource Resource, Representation Representation)
    {
        public string GetPath()
        {
            return $"{this.Catalog.Id}/{this.Resource.Id}/{this.Representation.Id}";
        }
    }
}
