using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using System.Security.Claims;
using System.Security.Cryptography;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Services
{
    // https://jasonwatmore.com/post/2021/06/15/net-5-api-jwt-authentication-with-refresh-tokens
    // https://github.com/cornflourblue/dotnet-5-jwt-authentication-api

    internal interface INexusAuthenticationService
    {
        Task<TokenPair> GenerateTokenPairAsync(
            NexusUser user);

        Task<TokenPair> RefreshTokenAsync(
            RefreshToken token);

        Task RevokeTokenAsync(
            RefreshToken token);

        Task RevokeDescendantTokensAsync(
            RefreshToken token);

        string Internal_GenerateDataSourceAccessToken(
            NexusUser user);
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
            // new token pair
            var newAccessToken = GenerateAccessToken(user, _securityOptions.AccessTokenLifetime);
            var newRefreshToken = GenerateRefreshToken(user.Id);

            user.RefreshTokens.Add(newRefreshToken);

            // clear old tokens
            ClearOldTokens(user);

            // save changes
            await _dbService.UpdateUserAsync(user);

            return new TokenPair(newAccessToken, newRefreshToken.Token);
        }

        public async Task<TokenPair> RefreshTokenAsync(RefreshToken token)
        {
            var user = token.Owner;

            // new token pair
            var newAccessToken = GenerateAccessToken(token.Owner, _securityOptions.AccessTokenLifetime);
            var newRefreshToken = RotateToken(token);

            user.RefreshTokens.Add(newRefreshToken);

            // clear old tokens
            ClearOldTokens(token.Owner);

            // save changes
            await _dbService.UpdateUserAsync(token.Owner);

            return new TokenPair(newAccessToken, newRefreshToken.Token);
        }

        public async Task RevokeTokenAsync(RefreshToken token)
        {
            // revoke token
            InternalRevokeToken(token);

            // clear old tokens
            ClearOldTokens(token.Owner);

            // save changes
            await _dbService.UpdateUserAsync(token.Owner);
        }

        public async Task RevokeDescendantTokensAsync(RefreshToken token)
        {
            // revoke descendant tokens
            InternalRevokeDescendantTokens(token);

            // clear old tokens
            ClearOldTokens(token.Owner);

            // save changes
            await _dbService.UpdateUserAsync(token.Owner);
        }

        public string Internal_GenerateDataSourceAccessToken(NexusUser user)
        {
            return GenerateAccessToken(user, _securityOptions.SourceAccessTokenLifetime);
        }

        #endregion

        #region Helper Methods

        private string GenerateAccessToken(NexusUser user, TimeSpan accessTokenLifeTime)
        {
            var mandatoryClaims = new[]
            {
                new Claim(Claims.Subject, user.Id),
                new Claim(Claims.Name, user.Name)
            };

            var claims = user.Claims
                .Select(entry => new Claim(entry.Value.Type, entry.Value.Value));

            var claimsIdentity = new ClaimsIdentity(
                mandatoryClaims.Concat(claims),
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claimsIdentity,
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.Add(accessTokenLifeTime),
                SigningCredentials = _signingCredentials
            };

            var tokenHandler = new JsonWebTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return token;
        }

        private RefreshToken GenerateRefreshToken(string userId, DateTime expires = default)
        {
            if (expires.Equals(default))
                expires = DateTime.UtcNow.Add(_securityOptions.RefreshTokenLifetime);

            var randomBytes = RandomNumberGenerator.GetBytes(64);
            var token = $"{Uri.EscapeDataString(userId)}@{Convert.ToBase64String(randomBytes)}";

            return new RefreshToken(token, expires);
        }

        private RefreshToken RotateToken(RefreshToken refreshToken)
        {
            var newRefreshToken = GenerateRefreshToken(
                refreshToken.Owner.Id,
                refreshToken.Expires);

            InternalRevokeToken(refreshToken, newRefreshToken.Token);

            return newRefreshToken;
        }

        private void ClearOldTokens(NexusUser user)
        {
            user.RefreshTokens.RemoveAll(x =>
                !x.IsActive &&
                x.Created.Add(_securityOptions.TokenAbuseDetectionPeriod) <= DateTime.UtcNow);
        }

        private void InternalRevokeToken(RefreshToken token, string? replacedByToken = default)
        {
            token.Revoked = DateTime.UtcNow;
            token.ReplacedByToken = replacedByToken;
        }

        public void InternalRevokeDescendantTokens(RefreshToken token)
        {
            if (!string.IsNullOrEmpty(token.ReplacedByToken))
            {
                var descendantToken = token.Owner.RefreshTokens
                    .FirstOrDefault(current => current.Token == token.ReplacedByToken);

                if (descendantToken is not null)
                {
                    if (descendantToken.IsActive)
                        InternalRevokeToken(descendantToken);

                    else
                        InternalRevokeDescendantTokens(descendantToken);
                }
            }
        }

        #endregion
    }
}
