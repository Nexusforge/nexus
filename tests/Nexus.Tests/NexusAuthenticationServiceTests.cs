using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System.Collections.ObjectModel;
using System.IdentityModel.Tokens.Jwt;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Services
{
    public class NexusAuthenticationServiceTests
    {
        private static IOptions<SecurityOptions> _securityOptions = Options.Create(new SecurityOptions()
        {
            AccessTokenLifetime = TimeSpan.FromHours(1),
            RefreshTokenLifetime = TimeSpan.FromHours(1),
            TokenAbuseDetectionPeriod = TimeSpan.FromHours(1)
        });

        private static TokenValidationParameters _validationParameters = new TokenValidationParameters()
        {
            NameClaimType = Claims.Name,
            LifetimeValidator = (before, expires, token, parameters) => expires > DateTime.UtcNow,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateActor = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(SecurityOptions.DefaultSigningKey))
        };

        private NexusUser CreateUser(string name, params RefreshToken[] refreshTokens)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            return new NexusUser()
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Claims = new ReadOnlyDictionary<Guid, NexusClaim>(new Dictionary<Guid, NexusClaim>()),
                RefreshTokens = refreshTokens.ToList()
            };
        }

        [Fact]
        public async Task CanGenerateTokenPair()
        {
            // Arrange
            var expectedName = "foo";
            var user = CreateUser(expectedName);

            var dbService = Mock.Of<IDBService>();

            var authService = new NexusAuthenticationService(
                dbService,
                _securityOptions);

            var tokenHandler = new JwtSecurityTokenHandler();

            // Act
            var tokenPair = await authService.GenerateTokenPairAsync(user);

            // Assert
            Assert.Single(user.RefreshTokens);
            Assert.Equal(tokenPair.RefreshToken, user.RefreshTokens.First().Token);

            Assert.Equal(
                user.RefreshTokens.First().Expires,
                DateTime.UtcNow.Add(_securityOptions.Value.RefreshTokenLifetime),
                TimeSpan.FromMinutes(1));

            var principal = tokenHandler.ValidateToken(tokenPair.AccessToken, _validationParameters, out var _);
            var actualName = principal.Identity!.Name;

            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public async Task CanRefresh()
        {
            // Arrange
            var expectedName = "foo";
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddDays(1));
            var user = CreateUser(expectedName, storedRefreshToken);
            storedRefreshToken.Owner = user;

            var dbService = Mock.Of<IDBService>();

            var service = new NexusAuthenticationService(
                dbService,
                _securityOptions);

            var tokenHandler = new JwtSecurityTokenHandler();

            // Act
            var tokenPair = await service.RefreshTokenAsync(storedRefreshToken);

            // Assert
            Assert.Equal(2, user.RefreshTokens.Count);
            Assert.Equal(storedRefreshToken.Token, user.RefreshTokens[0].Token);
            Assert.Equal(tokenPair.RefreshToken, user.RefreshTokens[1].Token);
            Assert.Equal(user.RefreshTokens[0].ReplacedByToken, user.RefreshTokens[1].Token);
            Assert.Equal(user.RefreshTokens[1].Expires, user.RefreshTokens[0].Expires);

            var principal = tokenHandler.ValidateToken(tokenPair.AccessToken, _validationParameters, out var _);
            var actualName = principal.Identity!.Name;

            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public async Task CanRevoke()
        {
            // Arrange
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddDays(1));
            var user = CreateUser("foo", storedRefreshToken);
            storedRefreshToken.Owner = user;

            var dbService = Mock.Of<IDBService>();

            var service = new NexusAuthenticationService(
                dbService,
                _securityOptions);

            // Act
            await service.RevokeTokenAsync(storedRefreshToken);

            // Assert
            Assert.Single(user.RefreshTokens);
            Assert.Equal(storedRefreshToken.Token, user.RefreshTokens.First().Token);
            Assert.True(user.RefreshTokens.First().IsRevoked);
        }

        [Fact]
        public async Task CanRevokeDescendants()
        {
            // Arrange
            var storedRefreshToken0 = new RefreshToken("123", DateTime.UtcNow.AddDays(1));
            var storedRefreshToken1 = new RefreshToken("456", DateTime.UtcNow.AddDays(1));
            var storedRefreshToken2 = new RefreshToken("789", DateTime.UtcNow.AddDays(1));
            var storedRefreshToken3 = new RefreshToken("987", DateTime.UtcNow.AddDays(1));

            var user = CreateUser("foo", 
                storedRefreshToken0, storedRefreshToken1, storedRefreshToken2, storedRefreshToken3);

            storedRefreshToken0.Owner = user;

            storedRefreshToken1.Owner = user;
            storedRefreshToken1.Revoked = DateTime.UtcNow;
            storedRefreshToken1.ReplacedByToken = storedRefreshToken2.Token;

            storedRefreshToken2.Owner = user;
            storedRefreshToken2.Revoked = DateTime.UtcNow;
            storedRefreshToken2.ReplacedByToken = storedRefreshToken3.Token;

            storedRefreshToken3.Owner = user;

            var dbService = Mock.Of<IDBService>();

            var service = new NexusAuthenticationService(
                dbService,
                _securityOptions);

            // Act
            await service.RevokeDescendantTokensAsync(storedRefreshToken1);

            // Assert
            Assert.Equal(4, user.RefreshTokens.Count);
            Assert.Equal(storedRefreshToken0.Token, user.RefreshTokens[0].Token);
            Assert.Equal(storedRefreshToken1.Token, user.RefreshTokens[1].Token);
            Assert.Equal(storedRefreshToken2.Token, user.RefreshTokens[2].Token);
            Assert.Equal(storedRefreshToken3.Token, user.RefreshTokens[3].Token);

            Assert.False(user.RefreshTokens[0].IsExpired);
            Assert.False(user.RefreshTokens[0].IsRevoked);

            Assert.False(user.RefreshTokens[1].IsExpired);
            Assert.True(user.RefreshTokens[1].IsRevoked);
            Assert.Equal(user.RefreshTokens[1].ReplacedByToken, user.RefreshTokens[2].Token);

            Assert.False(user.RefreshTokens[2].IsExpired);
            Assert.True(user.RefreshTokens[2].IsRevoked);
            Assert.Equal(user.RefreshTokens[2].ReplacedByToken, user.RefreshTokens[3].Token);

            Assert.False(user.RefreshTokens[3].IsExpired);
            Assert.True(user.RefreshTokens[3].IsRevoked);
        }

        [Fact]
        public async Task CleansUpExpiredTokens()
        {
            // Arrange
            var expectedName = "foo";
            var activeToken = new RefreshToken("123", DateTime.UtcNow.AddDays(1));
            var expiredToken = new RefreshToken("456", DateTime.UtcNow);
            var revokedToken = new RefreshToken("789", DateTime.UtcNow.AddDays(1));
            var user = CreateUser(expectedName, activeToken, expiredToken, revokedToken);

            revokedToken.Revoked = DateTime.UtcNow;

            var dbService = Mock.Of<IDBService>();

            var securityOptions = _securityOptions.Value with
            {
                TokenAbuseDetectionPeriod = TimeSpan.Zero
            };

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(securityOptions));

            // Act
            await service.GenerateTokenPairAsync(user);

            // Assert
            Assert.Equal(2, user.RefreshTokens.Count);
            Assert.Contains(user.RefreshTokens, token => token.Token == activeToken.Token);
            Assert.DoesNotContain(user.RefreshTokens, token => token.Token == expiredToken.Token);
            Assert.DoesNotContain(user.RefreshTokens, token => token.Token == revokedToken.Token);
        }
    }
}