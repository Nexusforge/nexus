﻿using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class NexusIdentityProviderExtensions
    {
        public static IServiceCollection AddNexusIdentityProvider(
            this IServiceCollection services)
        {
            // entity framework
            services.AddDbContext<DbContext>(options =>
            {
                options.UseInMemoryDatabase("OpenIddict");
                options.UseOpenIddict();
            });

            // MVC
            services.AddMvcCore();

            // OpenIddict
            services.AddOpenIddict()

                .AddCore(options =>
                {
                    options
                        .UseEntityFrameworkCore()
                        .UseDbContext<DbContext>();
                })

                .AddServer(options =>
                {
                    options
                        .AllowAuthorizationCodeFlow()
                        .RequireProofKeyForCodeExchange();

                    options
                        .AddEphemeralEncryptionKey()
                        .AddEphemeralSigningKey();

                    options
                        .SetAuthorizationEndpointUris("/connect/authorize")
                        .SetLogoutEndpointUris("/connect/logout")
                        .SetTokenEndpointUris("/connect/token");

                    options
                        .RegisterScopes(
                            Scopes.OpenId, 
                            Scopes.Profile);

                    options
                        .UseAspNetCore()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableLogoutEndpointPassthrough()
                        .EnableTokenEndpointPassthrough();
                });

                services.AddHostedService<HostedService>();

            return services;
        }

        public static WebApplication UseNexusIdentityProvider(
            this WebApplication app)
        {
            // AuthorizationController.cs https://github.com/openiddict/openiddict-samples/blob/dev/samples/Balosar/Balosar.Server/Controllers/AuthorizationController.cs
            app.MapGet("/connect/authorize", async (
                HttpContext httpContext,
                [FromServices] IOpenIddictApplicationManager applicationManager,
                [FromServices] IOpenIddictAuthorizationManager authorizationManager) =>
            {
                // request
                var request = httpContext.GetOpenIddictServerRequest() ??
                    throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

                // client
                var clientId = request.ClientId ?? string.Empty;

                var client = await applicationManager.FindByClientIdAsync(clientId) ??
                    throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

                // subject
                var subject = "f9208f50-cd54-4165-8041-b5cd19af45a4";

                // principal
                var claims = new[]
                {
                    new Claim(Claims.Subject, subject),
                    new Claim(Claims.Name, "Star Lord"),
                };

                var principal = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        claims,
                        authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        nameType: Claims.Name,
                        roleType: Claims.Role));

                // authorization
                var authorizationsEnumerable = authorizationManager.FindAsync(
                    subject: subject,
                    client: (await applicationManager.GetIdAsync(client))!,
                    status: Statuses.Valid,
                    type: AuthorizationTypes.Permanent,
                    scopes: request.GetScopes());

                var authorizations = new List<object>();

                await foreach (var current in authorizationsEnumerable)
                    authorizations.Add(current);

                var authorization = authorizations
                    .LastOrDefault();

                if (authorization is null)
                {
                    authorization = await authorizationManager.CreateAsync(
                        principal: principal,
                        subject: subject,
                        client: (await applicationManager.GetIdAsync(client))!,
                        type: AuthorizationTypes.Permanent,
                        scopes: principal.GetScopes());
                }

                principal.SetAuthorizationId(await authorizationManager.GetIdAsync(authorization));

                // claims
                foreach (var claim in principal.Claims)
                {
                    claim.SetDestinations(Destinations.IdentityToken);
                }

                return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            });

            // AuthorizationController.cs https://github.com/openiddict/openiddict-samples/blob/dev/samples/Balosar/Balosar.Server/Controllers/AuthorizationController.cs
            app.MapPost("/connect/token", async (
                HttpContext httpContext,
                [FromServices] IOpenIddictApplicationManager applicationManager,
                [FromServices] IOpenIddictAuthorizationManager authorizationManager) =>
            {
                var request = httpContext.GetOpenIddictServerRequest() ??
                    throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

                if (request.IsAuthorizationCodeGrantType())
                {
                    var principal = (await httpContext
                        .AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme))
                        .Principal;

                    if (principal is null)
                    {
                        return Results.Forbid(
                            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
                            properties: new AuthenticationProperties(new Dictionary<string, string?>
                            {
                                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                            }));
                    }

                    // returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
                    return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                }

                throw new InvalidOperationException("The specified grant type is not supported.");
            });

            return app;
        }
    }

    internal class HostedService : IHostedService
    {
        private IServiceProvider _serviceProvider;

        public HostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            using var context = scope.ServiceProvider.GetRequiredService<DbContext>();
            await context.Database.EnsureCreatedAsync(cancellationToken);

            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            if (await manager.FindByClientIdAsync("nexus", cancellationToken) is null)
            {
#warning https://localhost:5001 should not be hardcoded
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "nexus",
                    ClientSecret = "nexus-secret",
                    DisplayName = "Nexus",
                    RedirectUris = { new Uri("https://localhost:5001/signin-oidc/nexus") },
                    PostLogoutRedirectUris = { new Uri("https://localhost:5001/signout-oidc/nexus") },
                    Permissions =
                    {
                        // endpoints
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.Token,
                        Permissions.Endpoints.Logout,
                        
                        // grant types
                        Permissions.GrantTypes.AuthorizationCode,

                        // response types
                        Permissions.ResponseTypes.Code,

                        // scopes
                        Permissions.Scopes.Profile
                    }
                }, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
