using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nexus.Core;
using Nexus.Services;
using System.Security.Claims;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class OpenIdConnectExtensions
    {
        public static IServiceCollection AddNexusAuthentication(
            this IServiceCollection services,
            SecurityOptions securityOptions)
        {
            if (!securityOptions.OidcProviders.Any())
                return services;

            var builder = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = securityOptions.OidcProviders.First().Scheme;
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

            foreach (var provider in securityOptions.OidcProviders)
            {
                builder.AddOpenIdConnect(provider.Scheme, provider.DisplayName, options =>
                {
                    options.Authority = provider.Authority;
                    options.ClientId = provider.ClientId;
                    options.ClientSecret = provider.ClientSecret;

                    options.CallbackPath = $"/signin-oidc/{provider.Scheme}";
                    options.ResponseType = OpenIdConnectResponseType.Code;

                    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                    if (environmentName == "Development")
                        options.RequireHttpsMetadata = false;

                    options.Events = new OpenIdConnectEvents()
                    {
                        OnTokenValidated = async context =>
                        {
                            // scopes
                            // https://openid.net/specs/openid-connect-basic-1_0.html#Scopes

                            // sub claim type will be mapped to ClaimTypes.NameIdentifier
                            // https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/6e7a53e241e4566998d3bf365f03acd0da699a31/src/System.IdentityModel.Tokens.Jwt/ClaimTypeMapping.cs#L59

                            var principal = context.Principal;

                            if (principal is null)
                                throw new Exception("The principal is null. This should never happen.");

                            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                                ?? throw new Exception("The name identifier claim is missing. This should never happen.");

                            var userName = principal.FindFirstValue(ClaimTypes.Name)
                                ?? throw new Exception("The name claim is missing.");

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
                                        Type = Claims.IS_ADMIN,
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
