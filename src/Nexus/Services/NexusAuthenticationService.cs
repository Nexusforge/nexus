using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Nexus.Services
{
    // https://jasonwatmore.com/post/2021/06/15/net-5-api-jwt-authentication-with-refresh-tokens
    // https://github.com/cornflourblue/dotnet-5-jwt-authentication-api

    internal interface INexusAuthenticationService
    {
        Task<(string?, string?, string?)> AuthenticateAsync(
            string userId, 
            string password);

        Task<(string?, string?, string?)> RefreshTokenAsync(
            string token);

        Task<string?> RevokeTokenAsync(
            string token);
    }

    internal class NexusAuthenticationService : INexusAuthenticationService
    {
        #region Fields

        private IDBService _dbService;
        private IdentityOptions _identityOptions;
        private SecurityOptions _securityOptions;
        private SigningCredentials _signingCredentials;

        #endregion

        #region Constructors

        public NexusAuthenticationService(
            IDBService dbService,
            IOptions<IdentityOptions> identityOptions,
            IOptions<SecurityOptions> securityOptions)
        {
            _dbService = dbService;
            _identityOptions = identityOptions.Value;
            _securityOptions = securityOptions.Value;

            var key = Convert.FromBase64String(_securityOptions.Base64JwtSigningKey);
            _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature);
        }

        #endregion

        #region Methods

        public async Task<(string?, string?, string?)> AuthenticateAsync(
            string userId,
            string password)
        {
            // get user
            var user = await _dbService.FindByIdAsync(userId);

            if (user is null)
                return (null, null, "The user does not exist.");

            // check if user e-mail address is confirmed
            var isConfirmed =
                !_identityOptions.SignIn.RequireConfirmedAccount ||
                await _dbService.IsEmailConfirmedAsync(user);

            if (!isConfirmed)
                return (null, null, "The e-mail address is not confirmed.");

            // sign in
            var signInResult = await _dbService.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);

            if (!signInResult.Succeeded)
                return (null, null, "Sign-in failed.");

            // generate token pair
            var (jwtToken, refreshToken) = await this.GenerateTokenPairAsync(user);

            // add refresh token
            user.RefreshTokens.Add(refreshToken);

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateAsync(user);

            return (jwtToken, refreshToken.Token, null);
        }

        public async Task<(string?, string?, string?)> RefreshTokenAsync(string token)
        {
            // get user
            var user = await _dbService.FindByTokenAsync(token);

            if (user is null)
                return (null, null, "Token not found.");

            // get token
            var refreshToken = user.RefreshTokens.FirstOrDefault(current => current.Token == token);

            if (refreshToken is null)
                return (null, null, "Token not found.");

            // check token
            if (refreshToken.IsExpired)
                return (null, null, "The refresh token has expired.");

            // generate new token pair
            var (newJwtToken, newRefreshToken) = await this.GenerateTokenPairAsync(user);

            // delete redeemed refresh token
            user.RefreshTokens.RemoveAll(current => current.Token == token);

            // add refresh token
            user.RefreshTokens.Add(newRefreshToken);

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateAsync(user);

            return (newJwtToken, newRefreshToken.Token, null);
        }

        public async Task<string?> RevokeTokenAsync(string token)
        {
            // get user
            var user = await _dbService.FindByTokenAsync(token);

            if (user is null)
                return "Token not found.";

            // remove token
            var count = user.RefreshTokens.Count;
            user.RefreshTokens.RemoveAll(current => current.Token == token);

            var error = user.RefreshTokens.Count != count
                ? null
                : "Token not found.";

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateAsync(user);

            return error;
        }

        #endregion

        #region Helper Methods

        private void ClearExpiredTokens(List<RefreshToken> refreshTokens)
        {
            refreshTokens.RemoveAll(current => current.IsExpired);
        }

        private async Task<(string, RefreshToken)> GenerateTokenPairAsync(NexusUser user)
        {
            // get user claims
            var claims = await _dbService.GetClaimsAsync(user);

            // generate a token pair
            var jwtToken = this.GenerateJwtToken(user.UserId, claims);
            var refreshToken = this.GenerateRefreshToken();

            // return response
            return (jwtToken, refreshToken);
        }

        private string GenerateJwtToken(string userId, IList<Claim> claims)
        {
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new []
                {
                    new Claim(ClaimTypes.Name, userId)
                }.Concat(claims)),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.Add(_securityOptions.JwtTokenLifeTime),
                SigningCredentials = _signingCredentials
            };

            var tokenHandler = new JsonWebTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return token;
        }

        private RefreshToken GenerateRefreshToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);
            var token = Convert.ToBase64String(randomBytes);
            var expires = DateTime.UtcNow.Add(_securityOptions.RefreshTokenLifeTime);

            return new RefreshToken(token, expires);
        }

        #endregion
    }
}
