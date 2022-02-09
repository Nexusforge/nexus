using System.Text;

namespace Nexus.Client
{
    internal abstract class NexusOpenApiClientBase
    {
        public NexusOpenApiClient Client { get; init; } = null!;

        protected Task PrepareRequestAsync(
            HttpClient client,
            HttpRequestMessage request,
            string url)
        {
            return Task.CompletedTask;
        }

        protected Task PrepareRequestAsync(
            HttpClient client, 
            HttpRequestMessage request,
            StringBuilder? urlBuilder)
        {
            return Task.CompletedTask;
        }

        protected Task ProcessResponseAsync(
            HttpClient client,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            return this.Client.ProcessResponseAsync(client, response, cancellationToken);
        }
    }
}