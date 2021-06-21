using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Core;
using System;
using System.Security.Claims;

namespace Nexus.Services
{
    public class UserManager
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

        public void Initialize()
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
                if (userManager.FindByNameAsync(rootUsername).Result == null)
                {
                    var user = new IdentityUser(rootUsername);
                    var result = userManager.CreateAsync(user, rootPassword).Result;

                    if (result.Succeeded)
                    {
                        // confirm account
                        var token = userManager.GenerateEmailConfirmationTokenAsync(user).Result;
                        userManager.ConfirmEmailAsync(user, token);

                        // add claim
                        var claim = new Claim(Claims.IS_ADMIN, "true");
                        userManager.AddClaimAsync(user, claim).Wait();

                        // remove default root user
                        if (rootUsername != SecurityOptions.DefaultRootUser)
                        {
                            var userToDelete = userManager.FindByNameAsync(SecurityOptions.DefaultRootUser).Result;

                            if (userToDelete != null)
                                userManager.DeleteAsync(userToDelete);
                        }
                    }
                    else
                    {
                        var _ = userManager.CreateAsync(
                            new IdentityUser(SecurityOptions.DefaultRootUser), SecurityOptions.DefaultRootPassword).Result;
                    }
                }

                // ensure there is a test user
                var defaultTestUsername = "test@nexus.org";
                var defaultTestPassword = "#test0/User1";

                if (userManager.FindByNameAsync(defaultTestUsername).Result == null)
                {
                    var user = new IdentityUser(defaultTestUsername);
                    var result = userManager.CreateAsync(user, defaultTestPassword).Result;

                    if (result.Succeeded)
                    {
                        // confirm account
                        var token = userManager.GenerateEmailConfirmationTokenAsync(user).Result;
                        userManager.ConfirmEmailAsync(user, token);

                        // add claim
                        var claim = new Claim(Claims.CAN_ACCESS_CATALOG, "/IN_MEMORY/TEST/ACCESSIBLE;/IN_MEMORY/TEST/RESTRICTED");
                        userManager.AddClaimAsync(user, claim).Wait();
                    }
                }
            }
        }
    }
}
