using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using System.Collections.ObjectModel;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers
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
        // [anonymous]
        // GET      /api/users/authentication-schemes
        // GET      /api/users/authenticate
        // GET      /api/users/signout
        // POST     /api/users/refresh-token
        // POST     /api/users/revoke-token

        // [authenticated]
        // GET      /api/users/me
        // POST     /api/users/generate-tokens

        // [privileged]
        // GET      /api/users
        // PUT      /api/users/{userId}/{claimId}
        // DELETE   /api/users/{userId}/{claimId}

        #region Fields

        private IDBService _dbService;
        private INexusAuthenticationService _authService;
        private SecurityOptions _securityOptions;
        private ILogger<UsersController> _logger;

        #endregion

        #region Constructors

        public UsersController(
            IDBService dBService,
            INexusAuthenticationService authService,
            IOptions<SecurityOptions> securityOptions,
            ILogger<NexusAuthenticationService> logger)
        {
            _dbService = dBService;
            _authService = authService;
            _securityOptions = securityOptions.Value;
            _logger = logger;
        }

        #endregion

        #region Anonymous

        /// <summary>
        /// Returns a list of available authentication schemes.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("authentication-schemes")]
        public List<AuthenticationSchemeDescription> GetAuthenticationSchemes()
        {
            var providers = _securityOptions.OidcProviders.Any()
                ? _securityOptions.OidcProviders
                : new List<OpenIdConnectProvider>() { AuthExtensions.DefaultProvider };

            return providers
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
        public async Task<ActionResult<TokenPair>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            // get token
            var token = await _dbService.FindTokenAsync(request.RefreshToken);

            if (token is null)
                return NotFound("Token not found.");

            // check token
            if (token.IsRevoked)
            {
                _logger.LogWarning($"Attempted reuse of revoked token of user {token.Owner.Name}.");
                _authService.RevokeDescendantTokens(token, user, ipAddress, $"Attempted reuse of revoked ancestor token: {token}");
            }

            if (!token.IsActive)
                return UnprocessableEntity("Invalid token.");
#error

            // refresh token
            var tokenPair = await _authService
                .RefreshTokenAsync(token);

            return tokenPair;
        }

        /// <summary>
        /// Revokes a refresh token.
        /// </summary>
        /// <param name="request">The revoke token request.</param>
        [AllowAnonymous]
        [HttpPost("revoke-token")]
        public async Task<ActionResult> RevokeTokenAsync(RevokeTokenRequest request)
        {
            // get token
            var token = await _dbService.FindTokenAsync(request.Token);

            if (token is null)
                return NotFound("Token not found.");

            if (!token.IsActive)
                return UnprocessableEntity("The token is inactive.");

            await _authService
                .RevokeTokenAsync(token);

            return Ok();
        }

        #endregion

        #region Authenticated

        /// <summary>
        /// Gets the current user.
        /// </summary>
        [HttpGet("me")]
        public async Task<ActionResult<NexusUser>> GetMeAsync()
        {
            var userId = User.FindFirst(Claims.Subject)!.Value;          
            var user = await _dbService.FindUserAsync(userId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            return user;
        }

        /// <summary>
        /// Generates a set of tokens.
        /// </summary>
        [HttpPost("generate-tokens")]
        public async Task<ActionResult<TokenPair>> GenerateTokensAsync()
        {
            var userId = User.FindFirst(Claims.Subject)!.Value;
            var user = await _dbService.FindUserAsync(userId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            var tokenPair = await _authService.GenerateTokenPairAsync(user);

            return tokenPair;
        }

        #endregion

        #region Privileged

        /// <summary>
        /// Gets a list of users.
        /// </summary>
        /// <returns></returns>
        [Authorize(Policy = Policies.RequireAdmin)]
        [HttpGet]
        public async Task<ActionResult<List<NexusUser>>> GetUsersAsync()
        {
            var users = await _dbService.GetUsers()
                .ToListAsync();

            return users;
        }

        /// <summary>
        /// Puts a claim.
        /// </summary>
        /// <param name="userId">The identifier of the user.</param>
        /// <param name="claimId">The identifier of claim.</param>
        /// <param name="claim">The claim to put.</param>
        [Authorize(Policy = Policies.RequireAdmin)]
        [HttpPut("{userId}/{claimId}")]
        public async Task<ActionResult> PutClaimAsync(
            string userId,
            Guid claimId, 
            [FromBody] NexusClaim claim)
        {
#warning Is this thread safe? Maybe yes, because of scoped EF context.

            var user = await _dbService.FindUserAsync(userId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            var newClaims = user.Claims
                .ToDictionary(current => current.Key, current => current.Value);

            newClaims[claimId] = claim;
            user.Claims = new ReadOnlyDictionary<Guid, NexusClaim>(newClaims);

            await _dbService.UpdateUserAsync(user);

            return Ok();
        }

        /// <summary>
        /// Deletes a claim.
        /// </summary>
        /// <param name="userId">The identifier of the user.</param>
        /// <param name="claimId">The identifier of the claim.</param>
        [Authorize(Policy = Policies.RequireAdmin)]
        [HttpDelete("{userId}/{claimId}")]
        public async Task<ActionResult> DeleteClaimAsync(
            string userId, 
            Guid claimId)
        {
#warning Is this thread safe? Maybe yes, because of scoped EF context.

            var user = await _dbService.FindUserAsync(userId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            var newClaims = user.Claims
                .ToDictionary(current => current.Key, current => current.Value);

            newClaims.Remove(claimId);
            user.Claims = new ReadOnlyDictionary<Guid, NexusClaim>(newClaims);

            await _dbService.UpdateUserAsync(user);

            return Ok();
        }

        #endregion
    }
}
