using Nexus.Client;

namespace Nexus.BlazorWasm.Services
{
    public class ClientProvider
    {
        public ClientProvider(HttpClient httpClient)
        {
            Client = new NexusClient(httpClient);
        }

        public NexusClient Client { get; set; } 
    }
}
