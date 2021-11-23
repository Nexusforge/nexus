using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

internal abstract class NexusOpenApiClientBase
{
    public string BearerToken { get; private set; }

    public void SetBearerToken(string token)
    {
        BearerToken = token;
    }

    protected Task<HttpRequestMessage> CreateHttpRequestMessageAsync(CancellationToken cancellationToken)
    {
        var msg = new HttpRequestMessage();
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

        return Task.FromResult(msg);
    }
}