using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
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

        public async Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string username)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<NexusUser>>();
                var user = await userManager.FindByNameAsync(username);

                if (user is null)
                    return null;

                var claims = await userManager.GetClaimsAsync(user);
                claims.Add(new Claim(ClaimTypes.Name, username));

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Fake authentication type"));

                return principal;
            }
        }
    }
}
