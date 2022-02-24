// 0 = Namespace
// 1 = ClientName
// 2 = NexusConfigurationHeaderKey
// 3 = AuthorizationHeaderKey
// 4 = SubClientFields
// 5 = SubClientFieldAssignment
// 6 = SubClientProperties
// 7 = SubClientSource
// 8 = ExceptionType
// 9 = Models

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace {0};

/// <summary>
/// The OpenAPI client for the Nexus system.
/// </summary>
public class {1}
{
    private static string _tokenFolderPath = Path.Combine(Path.GetTempPath(), "nexus", "tokens");


    private static JsonSerializerOptions _options;

    private const string NexusConfigurationHeaderKey = "{2}";
    private const string AuthorizationHeaderKey = "{3}";

    private TokenPair? _tokenPair;
    private string? _tokenFilePath;

    private HttpClient _httpClient;

{4}
    static {1}()
    {
        _options = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        _options.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="{1}"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    public {1}(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl })
    {
        //
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="{1}"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    public {1}(HttpClient httpClient)
    {
        if (httpClient.BaseAddress is null)
            throw new Exception("The base address of the HTTP client must be set.");

        _httpClient = httpClient;

{5}
    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _tokenPair is not null;

{6}

    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="tokenPair">A pair of access and refresh tokens.</param>
    /// <returns>A task.</returns>
    public void SignIn(TokenPair tokenPair)
    {
        _tokenFilePath = Path.Combine(_tokenFolderPath, Uri.EscapeDataString(tokenPair.RefreshToken) + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            tokenPair = JsonSerializer.Deserialize<TokenPair>(File.ReadAllText(_tokenFilePath), _options);
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
        }

        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, $"Bearer {tokenPair.AccessToken}");

        _tokenPair = tokenPair;
    }

    /// <summary>
    /// Attaches configuration data to subsequent Nexus OpenAPI requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    public IDisposable AttachConfiguration(IDictionary<string, string> configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(NexusConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <summary>
    /// Clears configuration data for all subsequent Nexus OpenAPI requests.
    /// </summary>
    public void ClearConfiguration()
    {
        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
    }

    internal async Task<T> InvokeAsync<T>(string method, string relativeUrl, string? acceptHeaderValue, object? content, CancellationToken cancellationToken)
    {
        // prepare request
        var httpContent = content is null
            ? default
            : JsonContent.Create(content, options: _options);

        using var request = BuildRequestMessage(method, relativeUrl, httpContent);

        if (acceptHeaderValue is not null)
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // send request
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized && _tokenPair is not null)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        using var newRequest = BuildRequestMessage(method, relativeUrl, httpContent);

                        try
                        {
                            var newResponse = await RefreshTokenAsync(response, newRequest, cancellationToken);

                            if (newResponse is not null)
                            {
                                response.Dispose();
                                response = newResponse;
                                signOut = false;
                            }
                        }
                        catch
                        {
                            //
                        }
                    }
                }

                if (signOut)
                    SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                var statusCode = $"N00.{response.StatusCode}";

                if (string.IsNullOrWhiteSpace(message))
                    throw new {8}(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

                else
                    throw new {8}(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
            }
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            if (typeof(T) == typeof(object))
            {
                return default;
            }

            else if (typeof(T) == typeof(StreamResponse))
            {
                return (T)(object)(new StreamResponse(response, stream));
            }

            else
            {
                var returnValue = await JsonSerializer.DeserializeAsync<T>(stream, _options);

                if (returnValue is null)
                    throw new {8}($"N01", "Response data could not be deserialized.");

                return returnValue;
            }
        }
        finally
        {
            if (typeof(T) == typeof(StreamResponse))
                response.Dispose();
        }
    }
    
    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? httpContent)
    {
        return new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(relativeUrl, UriKind.Relative),
            Content = httpContent
        };
    }

    private async Task<HttpResponseMessage?> RefreshTokenAsync(
        HttpResponseMessage response, 
        HttpRequestMessage newRequest,
        CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        if (_tokenPair is null || response.RequestMessage is null)
            throw new Exception("Refresh token or request message is null. This should never happen.");

        var refreshRequest = new RefreshTokenRequest(RefreshToken: _tokenPair.RefreshToken);
        var tokenPair = await Users.RefreshTokenAsync(refreshRequest);

        if (_tokenFilePath is not null)
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
        }

        var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _tokenPair = tokenPair;

        return await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _tokenPair = null;
    }
}

{7}public class StreamResponse : IDisposable
{
    HttpResponseMessage _response;

    public StreamResponse(HttpResponseMessage response, Stream stream)
    {
        _response = response;

        Stream = stream;
    }

    public Stream Stream { get; }

    public void Dispose()
    {
        Stream.Dispose();
        _response.Dispose();
    }
}

public class {8} : Exception
{
    public {8}(string statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public string StatusCode { get; }
}

internal class DisposableConfiguration : IDisposable
{
    private {1} _client;

    public DisposableConfiguration({1} client)
    {
        _client = client;
    }

    public void Dispose()
    {
        _client.ClearConfiguration();
    }
}

{9}

