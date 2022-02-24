using Moq;
using Moq.Protected;
using Nexus.Client;
using System.Net;
using System.Text;
using System.Text.Json;
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

            var tokenPair = new TokenPair(
                AccessToken: "123",
                RefreshToken: "456"
            );

            // -> get catalogs (1st try)
            var tryCount = 0;

            var catalogsResponseMessage1 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.Unauthorized
            };

            catalogsResponseMessage1.Headers.Add("WWW-Authenticate", "Bearer The token expired at ...");

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("catalogs") && tryCount == 0),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var actual = requestMessage.Headers.Authorization!;
                    Assert.Equal($"Bearer {tokenPair.AccessToken}", $"{actual.Scheme} {actual.Parameter}");
                    catalogsResponseMessage1.RequestMessage = requestMessage;
                    tryCount++;
                })
                .ReturnsAsync(catalogsResponseMessage1);

            // -> refresh token
            var newTokenPair = new TokenPair(
                AccessToken: "123",
                RefreshToken: "456"
            );

            var refreshTokenResponseJsonString = JsonSerializer.Serialize(newTokenPair);

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
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().EndsWith("refresh-token")),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var refreshTokenRequest = JsonSerializer.Deserialize<RefreshTokenRequest>(requestMessage.Content!.ReadAsStream());
                    Assert.Equal(tokenPair.RefreshToken, refreshTokenRequest!.RefreshToken);
                })
                .ReturnsAsync(refreshTokenResponseMessage);

            // -> get catalogs (2nd try)
            var catalogId = "my-catalog-id";
            var expectedCatalog = new ResourceCatalog(Id: catalogId, default, default);

            var catalogsResponseMessage2 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("catalogs") && tryCount == 1),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var actual = requestMessage.Headers.Authorization!;
                    Assert.Equal($"Bearer {newTokenPair.AccessToken}", $"{actual.Scheme} {actual.Parameter}");
                })
                .ReturnsAsync(catalogsResponseMessage2);

            // -> http client
            var httpClient = new HttpClient(messageHandlerMock.Object)
            {
                BaseAddress = new Uri("http://localhost")
            };

            // -> OpenApi client
            var client = new NexusOpenApiClient(httpClient);

            // Act
            client.SignIn(tokenPair);
            var actualCatalog = await client.Catalogs.GetCatalogAsync(catalogId);

            // Assert
            Assert.Equal(
                JsonSerializer.Serialize(expectedCatalog),
                JsonSerializer.Serialize(actualCatalog));
        }
    }
}
