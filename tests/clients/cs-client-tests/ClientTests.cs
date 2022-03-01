using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Nexus.Client.Tests
{
    public class ClientTests
    {
        public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

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

            // -> API client
            var client = new NexusClient(httpClient);

            // Act
            client.SignIn(tokenPair);
            var actualCatalog = await client.Catalogs.GetCatalogAsync(catalogId);

            // Assert
            Assert.Equal(
                JsonSerializer.Serialize(expectedCatalog),
                JsonSerializer.Serialize(actualCatalog));
        }

        [Fact]
        public async Task CanAddConfigurationAsync()
        {
            // Arrange
            var messageHandlerMock = new Mock<HttpMessageHandler>();
            var catalogId = "my-catalog-id";
            var expectedCatalog = new ResourceCatalog(Id: catalogId, default, default);

            var actualHeaders = new List<IEnumerable<string>?>();

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    requestMessage.Headers.TryGetValues(NexusConfigurationHeaderKey, out var headers);
                    actualHeaders.Add(headers);
                })
                .ReturnsAsync(() =>
                {
                    return new HttpResponseMessage()
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
                    };
                });

            // -> http client
            var httpClient = new HttpClient(messageHandlerMock.Object)
            {
                BaseAddress = new Uri("http://localhost")
            };

            // -> API client
            var client = new NexusClient(httpClient);

            // -> configuration
            var configuration = new Dictionary<string, string>
            {
                ["foo1"] = "bar1",
                ["foo2"] = "bar2"
            };

            // Act
            _ = await client.Catalogs.GetCatalogAsync(catalogId);

            using (var disposable = client.AttachConfiguration(configuration))
            {
                _ = await client.Catalogs.GetCatalogAsync(catalogId);
            }

            _ = await client.Catalogs.GetCatalogAsync(catalogId);

            // Assert
            var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

            Assert.Collection(actualHeaders,
                headers => Assert.Null(headers),
                headers => Assert.Collection(headers, header => Assert.Equal(encodedJson, header)),
                headers => Assert.Null(headers));
        }
    }
}
