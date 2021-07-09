namespace Nexus.DataModel
{
    public record RepresentationRecord(ResourceCatalog Catalog, Resource Resource, Representation Representation)
    {
        public string GetPath()
        {
            return $"{this.Catalog.Id}/{this.Resource.Id}/{this.Representation.Id}";
        }
    }
}
