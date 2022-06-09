namespace Nexus.ClientGenerator
{
    public record GeneratorSettings(
        string? Namespace, 
        string ClientName,
        string ExceptionType);
}