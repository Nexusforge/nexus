namespace Nexus.DataModel
{
    /// <summary>
    /// Specifies the Nexus data type.
    /// </summary>
    public enum NexusDataType : ushort
    {
        /// <summary>
        /// Unsigned 8-bit integer.
        /// </summary>
        UINT8 = 0x108,

        /// <summary>
        /// Signed 8-bit integer.
        /// </summary>
        INT8 = 0x208,

        /// <summary>
        /// Unsigned 16-bit integer.
        /// </summary>
        UINT16 = 0x110,

        /// <summary>
        /// Signed 16-bit integer.
        /// </summary>
        INT16 = 0x210,

        /// <summary>
        /// Unsigned 32-bit integer.
        /// </summary>
        UINT32 = 0x120,

        /// <summary>
        /// Signed 32-bit integer.
        /// </summary>
        INT32 = 0x220,

        /// <summary>
        /// Unsigned 64-bit integer.
        /// </summary>
        UINT64 = 0x140,

        /// <summary>
        /// Signed 64-bit integer.
        /// </summary>
        INT64 = 0x240,

        /// <summary>
        /// 32-bit floating-point number.
        /// </summary>
        FLOAT32 = 0x320,

        /// <summary>
        /// 64-bit floating-point number.
        /// </summary>
        FLOAT64 = 0x340
    }

    /// <summary>
    /// Specifies the merge mode.
    /// </summary>
    public enum MergeMode
    {
        /// <summary>
        /// Properties of the items to be merged must be unique.
        /// </summary>
        ExclusiveOr,

        /// <summary>
        /// Properties of the original item get overriden by existing properties of the item to be merged.
        /// </summary>
        NewWins
    }

    /// <summary>
    /// A catalog item consists of a catalog, a resource and a representation.
    /// </summary>
    /// <param name="Catalog">The catalog.</param>
    /// <param name="Resource">The resource.</param>
    /// <param name="Representation">The representation.</param>
    public record CatalogItem(ResourceCatalog Catalog, Resource Resource, Representation Representation)
    {
        /// <summary>
        /// Construct a full qualified path.
        /// </summary>
        /// <returns>The full qualified path.</returns>
        public string ToPath()
        {
            return $"{this.Catalog.Id}/{this.Resource.Id}/{this.Representation.Id}";
        }
    }
}
