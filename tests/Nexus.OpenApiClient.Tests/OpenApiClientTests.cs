using Moq;
using Moq.Protected;
using Nexus.Client;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Nexus.OpenApiClient.Tests
{
    public class OpenApiClientTests
    {
        [Fact]
        public async Task CanAuthenticateAndRefreshAsync()
        {
            // Arrange
            var messageHandlerMock = new Mock<HttpMessageHandler>();

            // -> authenticate
            var authenticateResponse = new AuthenticateResponse()
            {
                JwtToken = "123",
                RefreshToken = "456"
            };

            var authenticateResponseMessage = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    JsonSerializer.Serialize(authenticateResponse),
                    Encoding.UTF8,
                    "application/json")
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.ToString().EndsWith("authenticate")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(authenticateResponseMessage);

            // -> get catalogs (1st try)
            var tryCount = 0;

            var catalogsResponseMessage1 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("Lifetime validation failed.", Encoding.UTF8, "text/plain"),
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.ToString().Contains("catalogs") && tryCount == 0),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                { 
                    catalogsResponseMessage1.RequestMessage = requestMessage;
                    tryCount++;
                })
                .ReturnsAsync(catalogsResponseMessage1);

            // -> refresh token
            var refreshTokenResponseJsonString = JsonSerializer.Serialize(new RefreshTokenResponse());

            var refreshTokenResponseMessage = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    refreshTokenResponseJsonString,
                    Encoding.UTF8, 
                    "application/json")
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.ToString().EndsWith("refresh-token")),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var refreshTokenRequest = JsonSerializer.Deserialize<RefreshTokenRequest>(requestMessage.Content.ReadAsStream());
                    Assert.Equal(authenticateResponse.RefreshToken, refreshTokenRequest.RefreshToken);
                })
                .ReturnsAsync(refreshTokenResponseMessage);

            // -> get catalogs (2nd try)
            var catalogId = "my-catalog-id";
            var expectedCatalog = new ResourceCatalog() { Id = catalogId };

            var catalogsResponseMessage2 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri.ToString().Contains("catalogs") && tryCount == 1),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(catalogsResponseMessage2);

            // -> http client
            var httpClient = new HttpClient(messageHandlerMock.Object);

            // -> OpenApi client
            var client = new NexusOpenApiClient("http://localhost", httpClient);

            // Act
            await client.PasswordSignInAsync("foo", "bar");
            var actualCatalog = await client.Catalogs.GetCatalogAsync(catalogId);

            // Assert
            Assert.Equal(
                JsonSerializer.Serialize(expectedCatalog),
                JsonSerializer.Serialize(actualCatalog));
        }
    }
}
