﻿using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using System;
using System.Security.Claims;

namespace Nexus.Services
{
    public class UserManager
    {
        private ILogger _logger;
        private IServiceProvider _serviceProvider;

        // Both, userDB and userManager, cannot be pulled in here because they are scoped
        public UserManager(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = loggerFactory.CreateLogger("Nexus Explorer");
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
                var defaultRootUsername = "root@nexus.org";
                var defaultRootPassword = "#root0/User1";

                string rootUsername = Environment.GetEnvironmentVariable("NEXUS_ROOT_USERNAME");
                string rootPassword = Environment.GetEnvironmentVariable("NEXUS_ROOT_PASSWORD");

                // fallback to default user
                if (string.IsNullOrWhiteSpace(rootUsername))
                    rootUsername = defaultRootUsername;
                
                // fallback to default password
                if (string.IsNullOrWhiteSpace(rootPassword))
                    rootPassword = defaultRootPassword;

                // ensure there is a root user
                if (userManager.FindByNameAsync(rootUsername).Result == null)
                {
                    var user = new IdentityUser(rootUsername);
                    var result = userManager.CreateAsync(user, rootPassword).Result;

                    if (result.Succeeded)
                    {
                        var claim = new Claim(Claims.IS_ADMIN, "true");
                        userManager.AddClaimAsync(user, claim).Wait();

                        // remove default root user
                        if (rootUsername != defaultRootUsername)
                        {
                            var userToDelete = userManager.FindByNameAsync(defaultRootUsername).Result;

                            if (userToDelete != null)
                                userManager.DeleteAsync(userToDelete);
                        }
                    }
                    else
                    {
                        var _ = userManager.CreateAsync(new IdentityUser(defaultRootUsername), defaultRootPassword).Result;
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
                        var claim = new Claim(Claims.CAN_ACCESS_PROJECT, "/IN_MEMORY/TEST/ACCESSIBLE;/IN_MEMORY/TEST/RESTRICTED");
                        userManager.AddClaimAsync(user, claim).Wait();
                    }
                }
            }
        }
    }
}
