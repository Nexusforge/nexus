using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace Services
{
    public class NexusAuthenticationServiceTests
    {
        const string Base64SigningKey = "/EaQ5vPn7YfXzA0Fz5PS3+mz11mYAbWeIWnETvobZHAqJQJDm3pKqxm/bVOJ1eOwcIK0w3+F6x6qZfI6rw6Lwg==";

        [Fact]
        public async Task CanAuthenticate()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>()
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .Returns<NexusUser, string, bool>((_, _, _) =>
               {
                   return Task.FromResult(SignInResult.Success);
               });

            Mock.Get(dbService)
                .Setup(s => s.GetClaimsAsync(
                    It.IsAny<NexusUser>()))
                .Returns<NexusUser>(_ =>
                {
                    return Task.FromResult((IList<Claim>)new List<Claim>() { });
                });

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions() 
                {
                    Base64JwtSigningKey = Base64SigningKey,
                    JwtTokenLifeTime = TimeSpan.FromHours(1),
                    RefreshTokenLifeTime = TimeSpan.FromHours(1)
                }));

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync("foo", "bar");

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);
            var actualNameClaim = token.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name);

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);

            Assert.Equal(expectedName, actualNameClaim.Value);
        }

        [Fact]
        public async Task AuthenticateErrorsForNotExistingUser()
        {
            // Arrange
            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(default(NexusUser)));

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .Returns(Task.FromResult(SignInResult.Failed));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
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

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>()
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            Mock.Get(dbService)
               .Setup(s => s.IsEmailConfirmedAsync(
                   It.IsAny<NexusUser>()))
               .Returns(Task.FromResult(false));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions() { VerifyEmail = true }),
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

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>()
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            Mock.Get(dbService)
               .Setup(s => s.CheckPasswordSignInAsync(
                   It.IsAny<NexusUser>(),
                   It.IsAny<string>(),
                   It.IsAny<bool>()))
               .Returns(Task.FromResult(SignInResult.Failed));

            var service = new NexusAuthenticationService(
                dbService, 
                Options.Create(new UsersOptions()),
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
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddHours(1));

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>() { storedRefreshToken }
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            Mock.Get(dbService)
                .Setup(s => s.GetClaimsAsync(
                    It.IsAny<NexusUser>()))
                .Returns(Task.FromResult((IList<Claim>)new List<Claim>() { }));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions()
                {
                    Base64JwtSigningKey = Base64SigningKey,
                    JwtTokenLifeTime = TimeSpan.FromHours(1),
                    RefreshTokenLifeTime = TimeSpan.FromHours(1)
                }));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync(expectedName, storedRefreshToken.Token);

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtToken);
            var actualNameClaim = token.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name);

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);

            Assert.Equal(expectedName, actualNameClaim.Value);
        }

        [Fact]
        public async Task RefreshErrorsForNotExistingUser()
        {
            // Arrange
            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(default(NexusUser)));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync("foo", "token");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The user does not exist.", error);
        }

        [Fact]
        public async Task RefreshErrorsForInvalidToken()
        {
            // Arrange
            var expectedName = "foo";
            var storedRefreshToken = new RefreshToken("validToken", DateTime.UtcNow.AddDays(1));

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>() { storedRefreshToken }
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions() { VerifyEmail = true }),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync(expectedName, "invalidToken");

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The refresh token is invalid.", error);
        }

        [Fact]
        public async Task RefreshErrorsForExpiredToken()
        {
            // Arrange
            var expectedName = "foo";
            var storedRefreshToken = new RefreshToken("validToken", DateTime.UtcNow);

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>() { storedRefreshToken }
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions() { VerifyEmail = true }),
                Options.Create(new SecurityOptions()));

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync(expectedName, storedRefreshToken.Token);

            // Assert
            Assert.Null(jwtToken);
            Assert.Null(refreshToken);

            Assert.Equal("The refresh token has expired.", error);
        }

        [Fact]
        public async Task CanRevoke()
        {
            // Arrange
            var expectedName = "foo";
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddHours(1));

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>() { storedRefreshToken }
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var error = await service.RevokeTokenAsync(expectedName, storedRefreshToken.Token);

            // Assert
            Assert.Null(error);
            Assert.Empty(user.RefreshTokens);
        }

        [Fact]
        public async Task RevokeErrorsForNotExistingUser()
        {
            // Arrange
            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(default(NexusUser)));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var error = await service.RevokeTokenAsync("foo", "token");

            // Assert
            Assert.Equal("The user does not exist.", error);
        }

        [Fact]
        public async Task RevokeErrorsForTokenNotFound()
        {
            // Arrange
            var expectedName = "foo";

            var user = new NexusUser()
            {
                UserName = expectedName,
                RefreshTokens = new List<RefreshToken>()
            };

            var dbService = Mock.Of<IDBService>();

            Mock.Get(dbService)
                .Setup(s => s.FindByIdAsync(
                    It.IsAny<string>()))
                .Returns(Task.FromResult(user));

            var service = new NexusAuthenticationService(
                dbService,
                Options.Create(new UsersOptions()),
                Options.Create(new SecurityOptions()));

            // Act
            var error = await service.RevokeTokenAsync("foo", "token");

            // Assert
            Assert.Equal("Token not found.", error);
        }
    }
}