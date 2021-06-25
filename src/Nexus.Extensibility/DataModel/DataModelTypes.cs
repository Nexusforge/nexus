namespace Nexus.DataModel
{
    public record DatasetRecord(Catalog Catalog, Channel Channel, Dataset Dataset)
    {
        public string GetPath()
        {
            return $"{this.Catalog.Id}/{this.Channel.Id}/{this.Dataset.Id}";
        }
    }
}
