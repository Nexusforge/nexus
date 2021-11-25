using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class UserManagerWrapper : IUserManagerWrapper
    {
        private ILogger _logger;
        private IServiceProvider _serviceProvider;
        private SecurityOptions _securityOptions;
        private PathsOptions _pathsOptions;

        public UserManagerWrapper(
            IServiceProvider serviceProvider,
            ILogger<UserManagerWrapper> logger, 
            IOptions<SecurityOptions> securityOptions,
            IOptions<PathsOptions> pathsOptions)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _securityOptions = securityOptions.Value;
            _pathsOptions = pathsOptions.Value;
        }

        public async Task InitializeAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userDB = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

                // database
                Directory.CreateDirectory(_pathsOptions.Config);

                if (userDB.Database.EnsureCreated())
                    _logger.LogInformation("SQLite database initialized");

                // ensure there is a root user
                var rootUsername = _securityOptions.RootUser;
                var rootPassword = _securityOptions.RootPassword;

                // ensure there is a root user
                if ((await userManager.FindByNameAsync(rootUsername)) == null)
                {
                    var user = new IdentityUser(rootUsername);
                    var result = await userManager.CreateAsync(user, rootPassword);

                    if (result.Succeeded)
                    {
                        // confirm account
                        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                        await userManager.ConfirmEmailAsync(user, token);

                        // add claim
                        var claim = new Claim(Claims.IS_ADMIN, "true");
                        await userManager.AddClaimAsync(user, claim);

                        // remove default root user
                        if (rootUsername != SecurityOptions.DefaultRootUser)
                        {
                            var userToDelete = await userManager.FindByNameAsync(SecurityOptions.DefaultRootUser);

                            if (userToDelete is not null)
                                await userManager.DeleteAsync(userToDelete);
                        }
                    }
                    else
                    {
                        await userManager.CreateAsync(
                            new IdentityUser(SecurityOptions.DefaultRootUser), SecurityOptions.DefaultRootPassword);
                    }
                }

                // ensure there is a test user
                var defaultTestUsername = "test@nexus.localhost";
                var defaultTestPassword = "#test0/User1";

                if ((await userManager.FindByNameAsync(defaultTestUsername)) == null)
                {
                    var user = new IdentityUser(defaultTestUsername);
                    var result = await userManager.CreateAsync(user, defaultTestPassword);

                    if (result.Succeeded)
                    {
                        // confirm account
                        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                        await userManager.ConfirmEmailAsync(user, token);

                        // add claim
                        var claim = new Claim(Claims.CAN_ACCESS_CATALOG, "/IN_MEMORY/TEST/ACCESSIBLE");
                        await userManager.AddClaimAsync(user, claim);
                    }
                }
            }
        }

        public async Task<ClaimsPrincipal?> GetClaimsPrincipalAsync(string username)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var user = await userManager.FindByNameAsync(username);

                if (user == null)
                    return null;

                var claims = await userManager.GetClaimsAsync(user);
                claims.Add(new Claim(ClaimTypes.Name, username));

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Fake authentication type"));

                return principal;
            }
        }
    }
}
