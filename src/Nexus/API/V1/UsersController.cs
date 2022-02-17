using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.API.V1;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
    /// <summary>
    /// Provides access to users.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class UsersController : ControllerBase
    {
        // /api/users
        // /api/users/authenticate
        // /api/users/refresh-token
        // /api/users/revoke-token
        // /api/users/{userId}/tokens

        // The endpoints "authenticate", "refresh-token" and "revoke-token"
        // contain sensitive information, therefore all parameters are passed
        // as part of the encrypted body. This is why these paths are not
        // beginning with /api/users/{userId}.

        #region Fields

        private IDBService _dBService;
        private INexusAuthenticationService _authService;
        private SecurityOptions _securityOptions;

        #endregion

        #region Constructors

        public UsersController(
            IDBService dBService,
            INexusAuthenticationService authService,
            IOptions<SecurityOptions> securityOptions)
        {
            _dBService = dBService;
            _authService = authService;
            _securityOptions = securityOptions.Value;
        }

        #endregion

        /// <summary>
        /// Gets a list of users.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = Policies.RequireAdmin)]
        [HttpGet]
        public async Task<ActionResult<List<string>>> GetUsersAsync()
        {
            var users = await _dBService.GetUsers()
                .Select(user => user.Id)
                .ToListAsync();

            return users;
        }

        /// <summary>
        /// Returns a list of available authentication schemes.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("authentication-schemes")]
        public List<AuthenticationSchemeDescription> GetAuthenticationSchemes()
        {
            return _securityOptions.OidcProviders
                .Select(provider => new AuthenticationSchemeDescription(provider.Scheme, provider.DisplayName))
                .ToList();
        }

        /// <summary>
        /// Authenticates the user.
        /// </summary>
        /// <param name="scheme">The authentication scheme to challenge.</param>
        /// <param name="returnUrl">The URL to return after successful authentication.</param>
        [AllowAnonymous]
        [HttpGet("authenticate")]
        public ChallengeResult Authenticate(
            [BindRequired] string scheme,
            [BindRequired] string returnUrl)
        {
            var properties = new AuthenticationProperties()
            {
                RedirectUri = returnUrl
            };

            return Challenge(properties, scheme);
        }

        /// <summary>
        /// Logs out the user.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("signout")]
        public async Task<RedirectResult> SignOutAsync(
            [BindRequired] string returnUrl)
        {
            // If called SignOut with a scheme, the user is forwarded to the identity providers
            // logout page. But that doesn't seem to be required here. Simply log out of Nexus.
            //
            // return SignOut(properties, scheme);

            await HttpContext.SignOutAsync();

            return Redirect(returnUrl);
        }

        /// <summary>
        /// Refreshes the JWT token.
        /// </summary>
        /// <param name="request">The refresh token request.</param>
        /// <returns>A new pair of JWT and refresh token.</returns>
        [AllowAnonymous]
        [HttpPost("refresh-token")]
        public async Task<ActionResult<RefreshTokenResponse>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            var (jwtToken, refreshToken, error) = await _authService
                .RefreshTokenAsync(request.RefreshToken);

            return new RefreshTokenResponse(jwtToken, refreshToken, error);
        }

        /// <summary>
        /// Revokes a refresh token.
        /// </summary>
        /// <param name="request">The revoke token request.</param>
        [AllowAnonymous]
        [HttpPost("revoke-token")]
        public async Task<ActionResult<RevokeTokenResponse>> RevokeTokenAsync(RevokeTokenRequest request)
        {
            var error = await _authService
                .RevokeTokenAsync(request.Token);

            return new RevokeTokenResponse(error);
        }

        /// <summary>
        /// Get a list of refresh tokens for the specified user.
        /// </summary>
        /// <param name="userId">The user to get the tokens for.</param>
        /// <returns>Returns the list of available refresh tokens.</returns>
        [HttpGet("{userId}/tokens")]
        public async Task<ActionResult<List<RefreshToken>>> GetRefreshTokensAsync(string userId)
        {
            // authorize
            var username = this.User.Identity?.Name;

            if (username is null)
                throw new Exception("This should never happen.");

            if (!(username == userId || this.HttpContext.User.HasClaim("IsAdmin", "true")))
                return this.Forbid("Only the user owning the refresh tokens and administrators can use this endpoint.");

            // get database user
            var dbUser = await _dBService.FindByIdAsync(userId);

            if (dbUser is null)
                return this.NotFound($"Could not find user {userId}.");

            return dbUser.RefreshTokens;
        }
    }
}
