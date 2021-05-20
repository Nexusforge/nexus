namespace Nexus.Extensibility
{
    public record DataSourceRegistration
    {
        public string RootPath { get; set; }
        public string DataSourceId { get; set; }
    }
}
