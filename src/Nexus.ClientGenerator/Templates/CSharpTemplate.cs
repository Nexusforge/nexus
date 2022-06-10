#nullable enable

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
// 10 = SubClientInterfaceProperties

using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace {{0}};

/// <summary>
/// A client for the Nexus system.
/// </summary>
public interface I{{1}}
{
{{10}}

    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>A task.</returns>
    Task SignInAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Attaches configuration data to subsequent Nexus API requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    IDisposable AttachConfiguration(object configuration);

    /// <summary>
    /// Clears configuration data for all subsequent Nexus API requests.
    /// </summary>
    void ClearConfiguration();
}

/// <inheritdoc />
public class {{1}} : I{{1}}, IDisposable
{
    private const string NexusConfigurationHeaderKey = "{{2}}";
    private const string AuthorizationHeaderKey = "{{3}}";

    private static string _tokenFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexusapi", "tokens");

    private TokenPair? _tokenPair;
    private HttpClient _httpClient;
    private string? _tokenFilePath;

{{4}}
    /// <summary>
    /// Initializes a new instance of the <see cref="{{1}}"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    public {{1}}(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl })
    {
        //
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="{{1}}"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    public {{1}}(HttpClient httpClient)
    {
        if (httpClient.BaseAddress is null)
            throw new Exception("The base address of the HTTP client must be set.");

        _httpClient = httpClient;

{{5}}
    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _tokenPair is not null;

{{6}}

    /// <inheritdoc />
    public async Task SignInAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        string actualRefreshToken;

        _tokenFilePath = Path.Combine(_tokenFolderPath, Uri.EscapeDataString(refreshToken) + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            actualRefreshToken = JsonSerializer.Deserialize<string>(File.ReadAllText(_tokenFilePath), Utilities.JsonOptions)
                ?? throw new Exception($"Unable to deserialize file {_tokenFilePath}.");
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(refreshToken, Utilities.JsonOptions));
            actualRefreshToken = refreshToken;
        }

        await RefreshTokenAsync(actualRefreshToken, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable AttachConfiguration(object configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(NexusConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <inheritdoc />
    public void ClearConfiguration()
    {
        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
    }

    internal async Task<T> InvokeAsync<T>(string method, string relativeUrl, string? acceptHeaderValue, string? contentTypeValue, HttpContent? content, CancellationToken cancellationToken)
    {
        // prepare request
        using var request = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);

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
                        try
                        {
                            await RefreshTokenAsync(_tokenPair.RefreshToken, cancellationToken);

                            using var newRequest = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);
                            var newResponse = await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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
                    throw new {{8}}(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

                else
                    throw new {{8}}(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
            }
        }

        try
        {
            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            else if (typeof(T) == typeof(StreamResponse))
            {
                return (T)(object)(new StreamResponse(response));
            }

            else
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var returnValue = await JsonSerializer.DeserializeAsync<T>(stream, Utilities.JsonOptions);

                if (returnValue is null)
                    throw new {{8}}($"N01", "Response data could not be deserialized.");

                return returnValue;
            }
        }
        finally
        {
            if (typeof(T) != typeof(StreamResponse))
                response.Dispose();
        }
    }
    
    private static readonly HttpRequestOptionsKey<bool> WebAssemblyEnableStreamingResponseKey = new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse");

    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? content, string? contentTypeHeaderValue, string? acceptHeaderValue)
    {
        var requestMessage = new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(relativeUrl, UriKind.Relative),
            Content = content
        };

        if (contentTypeHeaderValue is not null && requestMessage.Content is not null)
            requestMessage.Content.Headers.ContentType = MediaTypeWithQualityHeaderValue.Parse(contentTypeHeaderValue);

        if (acceptHeaderValue is not null)
            requestMessage.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // For web assembly
        // https://docs.microsoft.com/de-de/dotnet/api/microsoft.aspnetcore.components.webassembly.http.webassemblyhttprequestmessageextensions.setbrowserresponsestreamingenabled?view=aspnetcore-6.0
        // https://github.com/dotnet/aspnetcore/blob/0ee742c53f2669fd7233df6da89db5e8ab944585/src/Components/WebAssembly/WebAssembly/src/Http/WebAssemblyHttpRequestMessageExtensions.cs
        requestMessage.Options.Set(WebAssemblyEnableStreamingResponseKey, true);

        return requestMessage;
    }

    private async Task RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        var refreshRequest = new RefreshTokenRequest(refreshToken);
        var tokenPair = await Users.RefreshTokenAsync(refreshRequest, cancellationToken);

        if (_tokenFilePath is not null)
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair.RefreshToken, Utilities.JsonOptions));
        }

        var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _tokenPair = tokenPair;
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _tokenPair = default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

{{7}}

internal class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
     where TFrom : struct
     where TTo : struct
{
    private readonly Memory<TFrom> _from;

    public CastMemoryManager(Memory<TFrom> from) => _from = from;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

    public override void Unpin() => throw new NotSupportedException();
}

/// <summary>
/// A stream response. 
/// </summary>
public class StreamResponse : IDisposable
{
    private long _length;
    private HttpResponseMessage _response;

    internal StreamResponse(HttpResponseMessage response)
    {
        _response = response;
       
        if (_response.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            _length = contentLength;
        }
        else
        {
            _length = -1;
        }
    }

    /// <summary>
    /// Reads the data as an array of doubles.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public async Task<double[]> ReadAsDoubleAsync(CancellationToken cancellationToken = default)
    {
        if (_length < 0)
            throw new Exception("The data length is unknown.");

        if (_length % 8 != 0)
            throw new Exception("The data length is invalid.");

        var elementCount = _length / 8;
        var doubleBuffer = new double[elementCount];
        var byteBuffer = new CastMemoryManager<double, byte>(doubleBuffer).Memory;
        var stream = await _response.Content.ReadAsStreamAsync(cancellationToken);
        var remainingBuffer = byteBuffer;

        while (!remainingBuffer.IsEmpty)
        {
            var bytesRead = await stream.ReadAsync(remainingBuffer, cancellationToken);

            if (bytesRead == 0)
                throw new Exception("The stream ended early.");

            remainingBuffer = remainingBuffer.Slice(bytesRead);
        }

        return doubleBuffer;
    }

    /// <summary>
    /// Returns the underlying stream.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
    {
        return _response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response.Dispose();
    }
}

/// <summary>
/// A {{8}}.
/// </summary>
public class {{8}} : Exception
{
    internal {{8}}(string statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The exception status code.
    /// </summary>
    public string StatusCode { get; }
}

internal class DisposableConfiguration : IDisposable
{
    private {{1}} _client;

    public DisposableConfiguration({{1}} client)
    {
        _client = client;
    }

    public void Dispose()
    {
        _client.ClearConfiguration();
    }
}

{{9}}

internal static class Utilities
{
    internal static JsonSerializerOptions JsonOptions { get; }

    static Utilities()
    {
        JsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }
}