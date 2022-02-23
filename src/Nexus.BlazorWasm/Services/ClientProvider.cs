using Nexus.Client;

namespace Nexus.BlazorWasm.Services
{
    public class ClientProvider
    {
        public ClientProvider(HttpClient httpClient)
        {
            Client = new NexusOpenApiClient("https://localhost:8443", httpClient);
        }

        public NexusOpenApiClient Client { get; set; } 
    }
}
