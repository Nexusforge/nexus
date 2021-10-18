using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class UserManager
    {
        private ILogger _logger;
        private IServiceProvider _serviceProvider;
        private SecurityOptions _securityOptions;

        // Both, userDB and userManager, cannot be pulled in here because they are scoped
        public UserManager(IServiceProvider serviceProvider, ILoggerFactory loggerFactory, IOptions<SecurityOptions> securityOptions)
        {
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger("Nexus");
            _securityOptions = securityOptions.Value;
        }

        public async Task InitializeAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userDB = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

                // database
                if (userDB.Database.EnsureCreated())
                    _logger.LogInformation($"SQLite database initialized.");

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

                            if (userToDelete != null)
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
                var defaultTestUsername = "test@nexus.org";
                var defaultTestPassword = "#test0/User1";

                if ((await userManager.FindByNameAsync(defaultTestUsername)) == null)
                {
                    var user = new IdentityUser(defaultTestUsername);
                    var result = await userManager.CreateAsync(user, defaultTestPassword);

                    if (result.Succeeded)
                    {
                        // confirm account
                        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                        await userManager .ConfirmEmailAsync(user, token);

                        // add claim
                        var claim = new Claim(Claims.CAN_ACCESS_CATALOG, "/IN_MEMORY/TEST/ACCESSIBLE;/IN_MEMORY/TEST/RESTRICTED");
                        await userManager.AddClaimAsync(user, claim);
                    }
                }
            }
        }
    }
}
