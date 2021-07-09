namespace Nexus.DataModel
{
    public record DatasetRecord(Catalog Catalog, Resource Resource, Dataset Dataset)
    {
        public string GetPath()
        {
            return $"{this.Catalog.Id}/{this.Resource.Id}/{this.Dataset.Id}";
        }
    }
}
