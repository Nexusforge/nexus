using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client
{
    /// <summary>
    /// The OpenAPI client for the Nexus system.
    /// </summary>
    public class NexusOpenApiClient
    {
        private bool _isRefreshRequest;

        private string? _jwtToken;
        private string? _refreshToken;

        private HttpClient _httpClient;

        private CatalogsClient _catalogs;
        private DataClient _data;
        private JobsClient _jobs;
        private UsersClient _users;

        /// <summary>
        /// Initializes a new instances of the <see cref="NexusOpenApiClient"/>.
        /// </summary>
        /// <param name="baseUrl">The base URL to connect to.</param>
        /// <param name="httpClient">An optional HTTP client.</param>
        public NexusOpenApiClient(string baseUrl, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();

            _catalogs = new CatalogsClient(baseUrl, _httpClient) { Client = this };
            _data = new DataClient(baseUrl, _httpClient) { Client = this };
            _jobs = new JobsClient(baseUrl, _httpClient) { Client = this };
            _users = new UsersClient(baseUrl, _httpClient) { Client = this };
        }

        /// <summary>
        /// Gets a value which indicates if the user is authenticated.
        /// </summary>
        public bool IsAuthenticated => _jwtToken != null;

        /// <summary>
        /// Attempts to sign in the user.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="password">The user password.</param>
        /// <returns>A task.</returns>
        /// <exception cref="SecurityException">Thrown when the authentication fails.</exception>
        public async Task PasswordSignInAsync(string userId, string password)
        {
            var authenticateRequest = new AuthenticateRequest() { UserId = userId, Password = password };
            var authenticateResponse = await this.Users.AuthenticateAsync(authenticateRequest);

            if (authenticateResponse.Error is not null)
                throw new SecurityException($"Unable to authenticate. Reason: {authenticateResponse.Error}");

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticateResponse.JwtToken}");

            _jwtToken = authenticateResponse.JwtToken;
            _refreshToken = authenticateResponse.RefreshToken;
        }

#error add test for get all tokens, add test for expired tokens (clean up)
#error add "Configuration Header", Regex string    
#error Finish below implementation:

        internal async Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (!_isRefreshRequest)
            {
                // possible responses:
                // ___________________
                // not logged in:                       The current user is not authorized to access the catalog '/IN_MEMORY/TEST/RESTRICTED'.
                // invalid token:                       The bearer token could not be validated.
                // valid token but wrong signature:     Signature validation failed.
                // expired token:                       Lifetime validation failed.

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var reason = response.ReasonPhrase;

                    // new login required, maybe the server has restarted
                    if (reason == "Signature validation failed.")
                    {
                        _refreshToken = null;
                        _jwtToken = null;
                    }

                    else if (reason == "Lifetime validation failed.")
                    {
                        try
                        {
                            _isRefreshRequest = true;

                            var refreshRequest = new RefreshTokenRequest() { JwtToken = _jwtToken, RefreshToken = _refreshToken };
                            var refreshResponse = await this.Users.RefreshTokenAsync(refreshRequest);

                            if (refreshResponse.Error is null && response.RequestMessage != null)
                            {
                                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {refreshResponse.JwtToken}");

                                _jwtToken = refreshResponse.JwtToken;
                                _refreshToken = refreshResponse.RefreshToken;

                                var newResponse = await client.SendAsync(response.RequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                                response.Content = newResponse.Content;
                                response.RequestMessage = newResponse.RequestMessage;
                                response.StatusCode = newResponse.StatusCode;
                                response.ReasonPhrase = newResponse.ReasonPhrase;
                                response.Version = newResponse.Version;
                            }
                            else
                            {
                                _refreshToken = null;
                                _jwtToken = null;
                            }
                        }
                        finally
                        {
                            _isRefreshRequest = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the catalogs client.
        /// </summary>
        public ICatalogsClient Catalogs => _catalogs;

        /// <summary>
        /// Gets the data client.
        /// </summary>
        public IDataClient Data => _data;

        /// <summary>
        /// Gets the jobs client.
        /// </summary>
        public IJobsClient Jobs => _jobs;

        /// <summary>
        /// Gets the users client.
        /// </summary>
        public IUsersClient Users => _users;
    }
}