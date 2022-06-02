using Microsoft.AspNetCore.Identity;
using Nexus.Core;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Services
{
#warning The type is only required for the CatalogManager ("EnsureNoHierarchy") and hard to remove. But I would like to remove it.

    internal interface IUserManagerWrapper
    {
        Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string username);
    }

    internal class UserManagerWrapper : IUserManagerWrapper
    {
        private ILogger _logger;
        private IServiceProvider _serviceProvider;

        public UserManagerWrapper(
            IServiceProvider serviceProvider,
            ILogger<UserManagerWrapper> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string userId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<NexusUser>>();
                var user = await userManager.FindByNameAsync(userId);

                if (user is null)
                    return null;

                var claims = await userManager.GetClaimsAsync(user);
                claims.Add(new Claim(ClaimTypes.Name, userId));

                var principal = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        claims,
                        authenticationType: "Fake authentication type",
                        nameType: Claims.Name,
                        roleType: Claims.Role));

                return principal;
            }
        }
    }
}
