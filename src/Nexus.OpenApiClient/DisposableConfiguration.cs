using System;

namespace Nexus.Client
{
    internal class DisposableConfiguration : IDisposable
    {
        private NexusOpenApiClient _client;

        public DisposableConfiguration(NexusOpenApiClient client)
        {
            _client = client;
        }

        public void Dispose()
        {
            _client.ClearConfiguration();
        }
    }
}