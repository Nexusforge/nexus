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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security;
using System.Net;

namespace {0};

/// <summary>
/// The OpenAPI client for the Nexus system.
/// </summary>
public class {1}
{
    private static JsonSerializerOptions _options;

    private const string NexusConfigurationHeaderKey = "{2}";
    private const string AuthorizationHeaderKey = "{3}";

    private Uri _baseUrl;

    private string? _jwtToken;
    private string? _refreshToken;

    private HttpClient _httpClient;

{4}
    static {1}()
    {
        _options = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        };

        _options.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="{1}"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    /// <param name="httpClient">An optional HTTP client.</param>
    public {1}(string baseUrl, HttpClient? httpClient = null)
    {
        _baseUrl = new Uri(baseUrl);
        _httpClient = httpClient ?? new HttpClient();

{5}
    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _jwtToken != null;

{6}

    ///// <summary>
    ///// Attempts to sign in the user.
    ///// </summary>
    ///// <param name="userId">The user ID.</param>
    ///// <param name="password">The user password.</param>
    ///// <returns>A task.</returns>
    ///// <exception cref="SecurityException">Thrown when the authentication fails.</exception>
    //public async Task PasswordSignInAsync(string userId, string password)
    //{
    //    var authenticateRequest = new AuthenticateRequest(UserId: userId, Password: password);
    //    var authenticateResponse = await this.Users.AuthenticateAsync(authenticateRequest);

    //    if (authenticateResponse.Error is not null)
    //        throw new SecurityException($"Unable to authenticate. Reason: {authenticateResponse.Error}");

    //    _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, $"Bearer {authenticateResponse.JwtToken}");

    //    _jwtToken = authenticateResponse.JwtToken;
    //    _refreshToken = authenticateResponse.RefreshToken;
    //}

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

        using var request = this.BuildRequestMessage(method, relativeUrl, httpContent);

        if (acceptHeaderValue is not null)
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // send request
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        using var newRequest = this.BuildRequestMessage(method, relativeUrl, httpContent);
                        var newResponse = await this.RefreshTokenAsync(response, newRequest, cancellationToken);

                        if (newResponse is not null)
                        {
                            response.Dispose();
                            response = newResponse;
                            signOut = false;
                        }
                    }
                }

                if (signOut)
                    this.SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(message))
                    throw new NexusApiException($"The HTTP status code is {response.StatusCode}.");

                else
                    throw new NexusApiException($"The HTTP status code is {response.StatusCode}. The response message is: {message}");
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
                    throw new NexusApiException("Response data could not be deserialized.");

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
            RequestUri = new Uri(_baseUrl, relativeUrl),
            Content = httpContent
        };
    }

    private async Task<HttpResponseMessage?> RefreshTokenAsync(
        HttpResponseMessage response, 
        HttpRequestMessage newRequest,
        CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        if (_refreshToken is null || response.RequestMessage is null)
            throw new Exception("Refresh token or request message is null. This should never happen.");

        var refreshRequest = new RefreshTokenRequest(RefreshToken: _refreshToken);
        var refreshResponse = await this.Users.RefreshTokenAsync(refreshRequest);

        if (refreshResponse.Error is not null)
            return default;

        var authorizationHeaderValue = $"Bearer {refreshResponse.JwtToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _jwtToken = refreshResponse.JwtToken;
        _refreshToken = refreshResponse.RefreshToken;

        return await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _refreshToken = null;
        _jwtToken = null;
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
    public {8}(string message) : base(message)
    {
        //
    }
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

