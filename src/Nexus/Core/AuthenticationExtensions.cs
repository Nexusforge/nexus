﻿using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nexus.Core;
using Nexus.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class AuthenticationExtensions
    {
        public static OpenIdConnectProvider DefaultProvider { get; } = new OpenIdConnectProvider()
        {
            Scheme = "nexus",
            DisplayName = "Nexus",
            Authority = "https://localhost:8443",
            ClientId = "nexus",
            ClientSecret = "nexus-secret"
        };

        public static IServiceCollection AddNexusAuthentication(
            this IServiceCollection services,
            SecurityOptions securityOptions)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            var builder = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
            //.AddJwtBearer(options =>
            //{
            //    options.TokenValidationParameters = new TokenValidationParameters()
            //    {
            //        ClockSkew = TimeSpan.Zero,
            //        ValidateAudience = false,
            //        ValidateIssuer = false,
            //        ValidateActor = false,
            //        ValidateLifetime = true,
            //        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(securityOptions.Base64JwtSigningKey))
            //    };
            //});

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
                    options.ResponseType = OpenIdConnectResponseType.Code;

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

                            var userName = principal.FindFirstValue(Claims.Name)
                                ?? throw new Exception("The name claim is required.");

                            var userContext = context.HttpContext.RequestServices.GetRequiredService<UserDbContext>();

                            var user = await userContext.Users
                                .Include(user => user.Claims)
                                .SingleOrDefaultAsync(user =>
                                    user.Id == userId &&
                                    user.Scheme == context.Scheme.Name);

                            if (user is null)
                            {
                                user = new NexusUser()
                                {
                                    Id = userId,
                                    Name = userName,
                                    Scheme = context.Scheme.Name,
                                    Claims = new List<NexusClaim>(),
                                    RefreshTokens = new List<RefreshToken>()
                                };

                                var isFirstUser = !userContext.Users.Any();

                                if (isFirstUser)
                                {
                                    user.Claims.Add(new NexusClaim()
                                    {
                                        Type = NexusClaims.IS_ADMIN,
                                        Value = "true"
                                    });
                                }

                                userContext.Users.Add(user);
                            }

                            else
                            {
                                // user name may change, so update it
                                user.Name = userName;
                            }

                            await userContext.SaveChangesAsync();

                            var appIdentity = new ClaimsIdentity(user.Claims.Select(claim => new Claim(claim.Type, claim.Value)));
                            principal.AddIdentity(appIdentity);
                        }
                    };
                });
            }

            return services;
        }
    }
}
