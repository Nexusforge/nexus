using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System;
using System.Collections.Generic;
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
            var user = new NexusUser()
            {
                UserName = "foo",
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

            var userOptionsValue = new UsersOptions() { VerifyEmail = false };
            var usersOptions = Options.Create(userOptionsValue);

            var securityOptionsValue = new SecurityOptions()
            {
                Base64JwtSigningKey = Base64SigningKey,
                JwtTokenLifeTime = TimeSpan.FromHours(1),
                RefreshTokenLifeTime = TimeSpan.FromHours(1)
            };

            var securityOptions = Options.Create(securityOptionsValue);
            var service = new NexusAuthenticationService(dbService, usersOptions, securityOptions);

            // Act
            var (jwtToken, refreshToken, error) = await service.AuthenticateAsync("foo", "bar");

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);
        }

        [Fact]
        public async Task CanRefresh()
        {
            // Arrange
            var storedRefreshToken = new RefreshToken("123", DateTime.UtcNow.AddHours(1));

            var user = new NexusUser()
            {
                UserName = "foo",
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
                .Returns<NexusUser>(_ =>
                {
                    return Task.FromResult((IList<Claim>)new List<Claim>() { });
                });

            var userOptionsValue = new UsersOptions() { VerifyEmail = false };
            var usersOptions = Options.Create(userOptionsValue);

            var securityOptionsValue = new SecurityOptions()
            {
                Base64JwtSigningKey = Base64SigningKey,
                JwtTokenLifeTime = TimeSpan.FromHours(1),
                RefreshTokenLifeTime = TimeSpan.FromHours(1)
            };

            var securityOptions = Options.Create(securityOptionsValue);
            var service = new NexusAuthenticationService(dbService, usersOptions, securityOptions);

            // Act
            var (jwtToken, refreshToken, error) = await service.RefreshTokenAsync("foo", storedRefreshToken.Token);

            // Assert
            Assert.NotNull(jwtToken);
            Assert.NotNull(refreshToken);
            Assert.Null(error);

            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);
        }

#error Validate generated JWT token, add test for revoke, add test for get all tokens, add test for expired tokens (clean up, but should throw error)
#error add "Configuration Header", Regex string        
    }
}