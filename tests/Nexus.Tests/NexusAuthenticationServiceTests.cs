using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace Services
{
    public class NexusAuthenticationServiceTests
    {
        const string Base64JwtSigningKey = "/EaQ5vPn7YfXzA0Fz5PS3+mz11mYAbWeIWnETvobZHAqJQJDm3pKqxm/bVOJ1eOwcIK0w3+F6x6qZfI6rw6Lwg==";

        [Fact]
        public async Task CanAuthenticate()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>()
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAndSchemeAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .ReturnsAsync(SignInResult.Success);

            Mock.Get(dbService)
                .Setup(s => s.GetClaimsAsync(
                    It.IsAny<NexusUser>()))
                .ReturnsAsync(new List<Claim>());

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions() 
                {
                    Base64JwtSigningKey = Base64JwtSigningKey,
                    JwtTokenLifeTime = TimeSpan.FromHours(1),
                    RefreshTokenLifeTime = TimeSpan.FromHours(1)
                }));

            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters()
            {
                LifetimeValidator = (before, expires, token, parameters) => expires > DateTime.UtcNow,
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateActor = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Base64JwtSigningKey))
            };

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync("foo", "bar");

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);

            var principal = tokenHandler.ValidateToken(jwtToken, validationParameters, out var _);
            var actualName = principal.Identity!.Name;

            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public async Task AuthenticateErrorsForNotExistingUser()
        {
            // Arrange
            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAndSchemeAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(default(NexusUser));

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .ReturnsAsync(SignInResult.Failed);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync("foo", "bar");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The user does not exist.", error);
        }

        [Fact]
        public async Task AuthenticateErrorsForNonConfirmedEmailAddress()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>()
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAndSchemeAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            Mock.Get(dbService)
               .Setup(s => s.IsEmailConfirmedAsync(
                   It.IsAny<NexusUser>()))
               .ReturnsAsync(false);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions() { SignIn = new SignInOptions() { RequireConfirmedAccount = true } }),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync(expectedName, "bar");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The e-mail address is not confirmed.", error);
        }

        [Fact]
        public async Task AuthenticateErrorsForFailedSignin()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>()
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAndSchemeAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .ReturnsAsync(SignInResult.Failed);

            var service = new NexusAuthenticationService(
                dbService, 
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync(expectedName, "bar");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("Sign-in failed.", error);
        }

        [Fact]
        public async Task CanRefresh()
        {
            // Arrange
            var expectedName = "foo";
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddDays(1));

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>() { storedRefreshToken }
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByTokenAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            Mock.Get(dbService)
                .Setup(s => s.GetClaimsAsync(
                    It.IsAny<NexusUser>()))
                .ReturnsAsync(new List<Claim>());

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()
                {
                    Base64JwtSigningKey = Base64JwtSigningKey,
                    JwtTokenLifeTime = TimeSpan.FromHours(1),
                    RefreshTokenLifeTime = TimeSpan.FromHours(1)
                }));

            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters()
            {
                LifetimeValidator = (before, expires, token, parameters) => expires > DateTime.UtcNow,
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateActor = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Base64JwtSigningKey))
            };

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync(storedRefreshToken.Token);

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);

            var principal = tokenHandler.ValidateToken(jwtToken, validationParameters, out var _);
            var actualName = principal.Identity!.Name;

            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public async Task RefreshErrorsForTokenNotFound()
        {
            // Arrange
            var storedRefreshToken = new RefreshToken("validToken", DateTime.UtcNow.AddDays(1));

            var user = new NexusUser(
                UserId: "foo",
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>() { storedRefreshToken }
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByTokenAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync("invalidToken");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("Token not found.", error);
        }

        [Fact]
        public async Task RefreshErrorsForExpiredToken()
        {
            // Arrange
            var storedRefreshToken = new RefreshToken("validToken", DateTime.UtcNow);

            var user = new NexusUser(
                UserId: "foo",
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>() { storedRefreshToken }
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByTokenAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync(storedRefreshToken.Token);

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The refresh token has expired.", error);
        }

        [Fact]
        public async Task CanRevoke()
        {
            // Arrange
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddDays(1));

            var user = new NexusUser(
                UserId: "foo",
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>() { storedRefreshToken }
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByTokenAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var error = await service.RevokeTokenAsync(storedRefreshToken.Token);

            // Assert
            Assert.Null(error);
            Assert.Empty(user.RefreshTokens);
        }

        [Fact]
        public async Task RevokeErrorsForTokenNotFound()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>()
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByTokenAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var error = await service.RevokeTokenAsync("token");

            // Assert
            Assert.Equal("Token not found.", error);
        }

        [Fact]
        public async Task CleansUpExpiredTokens()
        {
            // Arrange
            var expectedName = "foo";
            var expiredToken = new RefreshToken("123", DateTime.UtcNow);

            var user = new NexusUser(
                UserId: expectedName,
                Claims: new List<Claim>(),
                RefreshTokens: new List<RefreshToken>()
            );

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAndSchemeAsync(
                    It.IsAny<string>()))
                .ReturnsAsync(user);

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .ReturnsAsync(SignInResult.Success);

            Mock.Get(dbService)
                .Setup(s => s.GetClaimsAsync(
                    It.IsAny<NexusUser>()))
                .ReturnsAsync(new List<Claim>());

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new IdentityOptions()),
                Options.Create(new SecurityOptions()
                {
                    RefreshTokenLifeTime = TimeSpan.FromHours(1)
                }));

            // Act
            await service.AuthenticateAsync("foo", "bar");

            // Assert
            Assert.DoesNotContain(user.RefreshTokens, token => token == expiredToken);
        }
    }
}