namespace Nexus.ClientGenerator
{
    public record GeneratorSettings(
        string Namespace, 
        string ClientName,
        string OutputFileName,
        string ExceptionType,
        string NexusConfigurationHeaderKey,
        string AuthorizationHeaderKey);
}