namespace Nexus.DataModel
{
    public record RepresentationRecord(Catalog Catalog, Resource Resource, Representation Representation)
    {
        public string GetPath()
        {
            return $"{this.Catalog.Id}/{this.Resource.Id}/{this.Representation.Id}";
        }
    }
}
