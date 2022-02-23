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
        Task<TokenPair> GenerateTokenPairAsync(
            NexusUser user);

        Task<TokenPair> RefreshTokenAsync(
            NexusUser user,
            string refreshTokenString);

        Task RevokeTokenAsync(
            NexusUser user,
            string refreshTokenString);
    }

    internal class NexusAuthenticationService : INexusAuthenticationService
    {
        #region Fields

        private IDBService _dbService;
        private SecurityOptions _securityOptions;
        private SigningCredentials _signingCredentials;

        #endregion

        #region Constructors

        public NexusAuthenticationService(
            IDBService dbService,
            IOptions<SecurityOptions> securityOptions)
        {
            _dbService = dbService;
            _securityOptions = securityOptions.Value;

            var key = Convert.FromBase64String(_securityOptions.Base64JwtSigningKey);
            _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature);
        }

        #endregion

        #region Methods

        public async Task<TokenPair> GenerateTokenPairAsync(
            NexusUser user)
        {
            // generate token pair
            var (accessToken, refreshToken) = this.InternalGenerateTokenPair(user);

            // add refresh token
            user.RefreshTokens.Add(refreshToken);

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateUserAsync(user);

            return new TokenPair(accessToken, refreshToken.Token);
        }

        public async Task<TokenPair> RefreshTokenAsync(NexusUser user, string refreshTokenString)
        {
            // generate new token pair
            var (newAccessToken, newRefreshToken) = this.InternalGenerateTokenPair(user);

            // delete redeemed refresh token
            user.RefreshTokens.RemoveAll(current => current.Token == refreshTokenString);

            // add refresh token
            user.RefreshTokens.Add(newRefreshToken);

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateUserAsync(user);

            return new TokenPair(newAccessToken, newRefreshToken.Token);
        }

        public async Task RevokeTokenAsync(NexusUser user, string refreshTokenString)
        {
            // remove token
            var count = user.RefreshTokens.Count;
            user.RefreshTokens.RemoveAll(current => current.Token == refreshTokenString);

            // clear expired tokens
            this.ClearExpiredTokens(user.RefreshTokens);

            // save changes
            await _dbService.UpdateUserAsync(user);
        }

        #endregion

        #region Helper Methods

        private void ClearExpiredTokens(List<RefreshToken> refreshTokens)
        {
            refreshTokens.RemoveAll(current => current.IsExpired);
        }

        private (string, RefreshToken) InternalGenerateTokenPair(NexusUser user)
        {
            // generate a token pair
            var accessToken = this.GenerateAccessToken(user.Id, user.Claims.Select(entry => entry.Value));
            var refreshToken = this.GenerateRefreshToken();

            // return response
            return (accessToken, refreshToken);
        }

        private string GenerateAccessToken(string userId, IEnumerable<NexusClaim> claims)
        {
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new []
                {
                    new Claim(ClaimTypes.Name, userId)
                }.Concat(claims.Select(claim => new Claim(claim.Type, claim.Value)))),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.Add(_securityOptions.AccessTokenLifeTime),
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
