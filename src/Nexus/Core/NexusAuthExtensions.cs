using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using System.Collections.ObjectModel;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class NexusAuthExtensions
    {
        public static OpenIdConnectProvider DefaultProvider { get; } = new OpenIdConnectProvider()
        {
            Scheme = "nexus",
            DisplayName = "Nexus",
            Authority = "https://localhost:8444",
            ClientId = "nexus",
            ClientSecret = "nexus-secret"
        };

        public static IServiceCollection AddNexusAuth(
            this IServiceCollection services,
            PathsOptions pathsOptions,
            SecurityOptions securityOptions)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(pathsOptions.Config, "data-protection-keys")));

            var builder = services

                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })

                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.ExpireTimeSpan = securityOptions.CookieLifetime;
                    options.SlidingExpiration = false;

                    options.Events.OnRedirectToAccessDenied = context => {
                        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                        return Task.CompletedTask;
                    };
                })

                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        NameClaimType = Claims.Name,
                        ClockSkew = TimeSpan.Zero,
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateActor = false,
                        ValidateLifetime = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(securityOptions.Base64JwtSigningKey))
                    };
                });

            var providers = securityOptions.OidcProviders.Any()
                ? securityOptions.OidcProviders
                : new List<OpenIdConnectProvider>() { DefaultProvider };

            foreach (var provider in providers)
            {
                builder.AddOpenIdConnect(provider.Scheme, provider.DisplayName, options =>
                {
                    options.Authority = provider.Authority;
                    options.ClientId = provider.ClientId;
                    options.ClientSecret = provider.ClientSecret;

                    options.CallbackPath = $"/signin-oidc/{provider.Scheme}";
                    options.SignedOutCallbackPath = $"/signout-oidc/{provider.Scheme}";
                    options.ResponseType = OpenIdConnectResponseType.Code;

                    options.TokenValidationParameters.AuthenticationType = provider.Scheme;
                    options.TokenValidationParameters.NameClaimType = Claims.Name;

                    /* user info endpoint is contacted AFTER OnTokenValidated, which requires the name claim to be present */
                    options.GetClaimsFromUserInfoEndpoint = false;

                    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                    if (environmentName == "Development")
                        options.RequireHttpsMetadata = false;

                    options.Events = new OpenIdConnectEvents()
                    {
                        OnTokenValidated = async context =>
                        {
                            // scopes
                            // https://openid.net/specs/openid-connect-basic-1_0.html#Scopes

                            var principal = context.Principal;

                            if (principal is null)
                                throw new Exception("The principal is null. This should never happen.");

                            var userId = principal.FindFirstValue(Claims.Subject)
                                ?? throw new Exception("The subject claim is missing. This should never happen.");

                            var username = principal.FindFirstValue(Claims.Name)
                                ?? throw new Exception("The name claim is required.");

                            var dbContext = context.HttpContext.RequestServices.GetRequiredService<UserDbContext>();
                            var uniqueUserId = $"{Uri.EscapeDataString(userId)}@{Uri.EscapeDataString(context.Scheme.Name)}";

                            // user
                            var user = await dbContext.Users
                                .SingleOrDefaultAsync(user => user.Id == uniqueUserId);

                            if (user is null)
                            {
                                var newClaims = new Dictionary<Guid, NexusClaim>();

                                user = new NexusUser()
                                {
                                    Id = uniqueUserId,
                                    Name = username,
                                    RefreshTokens = new List<RefreshToken>()
                                };

                                var isFirstUser = !dbContext.Users.Any();

                                if (isFirstUser)
                                    newClaims[Guid.NewGuid()] = new NexusClaim(NexusClaims.IS_ADMIN, "true");

                                user.Claims = new ReadOnlyDictionary<Guid, NexusClaim>(newClaims);
                                dbContext.Users.Add(user);
                            }

                            else
                            {
                                // user name may change, so update it
                                user.Name = username;
                            }

                            await dbContext.SaveChangesAsync();

                            // oicd identity
                            var oidcIdentity = (ClaimsIdentity)principal.Identity!;
                            var subClaim = oidcIdentity.FindFirst(Claims.Subject);
                            
                            if (subClaim is not null)
                                oidcIdentity.RemoveClaim(subClaim);

                            oidcIdentity.AddClaim(new Claim(Claims.Subject, uniqueUserId));

                            // app identity
                            var claims = user.Claims.Select(entry => new Claim(entry.Value.Type, entry.Value.Value));
                            var appIdentity = new ClaimsIdentity(claims, authenticationType: context.Scheme.Name);

                            principal.AddIdentity(appIdentity);
                        }
                    };
                });
            }

            var authenticationSchemes = new[]
            {
                CookieAuthenticationDefaults.AuthenticationScheme,
                JwtBearerDefaults.AuthenticationScheme
            };

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddAuthenticationSchemes(authenticationSchemes)
                    .Build();

                options
                    .AddPolicy(Policies.RequireAdmin, policy => policy
                    .RequireClaim(NexusClaims.IS_ADMIN, "true")
                    .AddAuthenticationSchemes(authenticationSchemes));
            });

            return services;
        }
    }
}
