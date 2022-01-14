using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexus.API.V1;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers.V1
{
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

        #endregion

        #region Constructors

        public UsersController(
            IDBService dBService,
            INexusAuthenticationService authService)
        {
            _dBService = dBService;
            _authService = authService;
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
                .Select(user => user.UserName)
                .ToListAsync();

            return this.Ok(users);
        }

        /// <summary>
        /// Authenticates the user.
        /// </summary>
        /// <param name="request">The authentication request.</param>
        /// <returns>A pair of JWT and refresh token.</returns>
        [AllowAnonymous]
        [HttpPost("authenticate")]
        public async Task<ActionResult<AuthenticateResponse>> AuthenticateAsync(AuthenticateRequest request)
        {
            var (jwtToken, refreshToken, error) = await _authService
                .AuthenticateAsync(request.UserId, request.Password);

            return this.Ok(new AuthenticateResponse(jwtToken, refreshToken, error));
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

            return this.Ok(new RefreshTokenResponse(jwtToken, refreshToken, error));
        }

        /// <summary>
        /// Revokes a refresh token.
        /// </summary>
        /// <param name="request">The revoke token request.</param>
        [AllowAnonymous]
        [HttpPost("revoke-token")]
        public async Task<ActionResult> RevokeTokenAsync(RevokeTokenRequest request)
        {
            var error = await _authService
                .RevokeTokenAsync(request.Token);

            return this.Ok(new RevokeTokenResponse(error));
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
            var user = this.HttpContext.User;

            if (!(user.Identity.Name == userId || this.HttpContext.User.HasClaim("IsAdmin", "true")))
                return this.Unauthorized();

            // get database user
            var dbUser = await _dBService.FindByIdAsync(userId);

            if (dbUser is null)
                return this.NotFound($"Could not find user {userId}.");

            return this.Ok(dbUser.RefreshTokens);
        }
    }
}
