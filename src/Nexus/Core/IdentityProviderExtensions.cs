﻿using Duende.IdentityServer;
using Duende.IdentityServer.Events;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Test;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Security.Policy;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class IdentityProviderExtensions
    {
        public static IServiceCollection AddNexusIdentityProvider(
            this IServiceCollection services)
        {
            services
                .AddIdentityServer()

                .AddInMemoryClients(new List<Client>
                {
                    new Client()
                    {
                        AllowedGrantTypes = GrantTypes.Code,
                        AllowedScopes = new List<string> 
                        { 
                            IdentityServerConstants.StandardScopes.OpenId,
                            IdentityServerConstants.StandardScopes.Profile
                        },
                        ClientId = "nexus",
                        ClientName = "Nexus",
                        ClientSecrets = new List<Secret>
                        { 
                            new Secret("nexus-secret")
                        },
                        RedirectUris = new List<string>
                        { 
                            "https://localhost:8443/signin-oidc/nexus"
                        }
                    }
                })

                .AddInMemoryIdentityResources(new List<IdentityResource>()
                {
                    new IdentityResources.OpenId(),
                    new IdentityResources.Profile(),
                })

                .AddTestUsers(new List<TestUser>
                {
                    new TestUser()
                    {
                        SubjectId = "b31b7c59-928d-4690-bbfb-3df1bfd4f923",
                        Username = "root@nexus.localhost",
                        Password = "password"
                    }
                });

            return services;
        }

        public static WebApplication UseNexusIdentityProvider(
           this WebApplication app)
        {
            app.UseIdentityServer();

            app.MapGet("/Account/Login", async (HttpContext context) =>
            {
                var returnUrl = context.Request.Query["ReturnUrl"];

                context.Response.Headers.ContentType = MediaTypeNames.Text.Html;

                await context.Response.WriteAsync($@"
<!DOCTYPE html>
<html lang=""en"">
  <head>
    <meta charset=""utf-8"">
    <title>Sign in</title>
  </head>
  <body>
    <form action=""/Account/Login"" method=""post"">
      <label for=""username"">Username:</label><br>
      <input type=""text"" id=""username"" name=""username"" value=""root@nexus.localhost"">
      <br>
      <label for=""password"">Password:</label><br>
      <input type=""password"" id=""password"" name=""password"" value=""password"">
      <br>
      <br>
      <input type=""hidden"" id=""returnUrl"" name=""returnUrl"" value=""{returnUrl}"" /> 
      <input type=""submit"" value=""Sign in"">
    </form> 
</form>
  </body>
</html>
");
            });

            app.MapPost("/Account/Login", async (
                HttpContext httpContext,
                [FromServices] TestUserStore users,
                [FromServices] IEventService events,
                [FromServices] IIdentityServerInteractionService interaction) =>
            {
                // [FromForm] binding is not working in .NET 6 and minimal API
                var content = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
                var parts = content.Split('&');

                var username = Uri.UnescapeDataString(parts[0].Split(new[] { '=' }, count: 2)[1]);
                var password = Uri.UnescapeDataString(parts[1].Split(new[] { '=' }, count: 2)[1]);
                var returnUrl = Uri.UnescapeDataString(parts[2].Split(new[] { '=' }, count: 2)[1]);

                var context = await interaction.GetAuthorizationContextAsync(returnUrl);

                if (users.ValidateCredentials(username, password))
                {
                    var user = users.FindByUsername(username);

                    await events.RaiseAsync(new UserLoginSuccessEvent(
                        user.Username, 
                        user.SubjectId, 
                        user.Username, 
                        clientId: context?.Client.ClientId));

                    var isuser = new IdentityServerUser(user.SubjectId)
                    {
                        DisplayName = user.Username
                    };

                    await httpContext.SignInAsync(isuser);

                    //if (context != null)
                        return Results.Redirect(returnUrl);

                    // request for a local page
#warning implement this
                    //if (Url.IsLocalUrl(returnUrl))
                    //{
                    //    return Results.Redirect(returnUrl);
                    //}

                    //else if (string.IsNullOrEmpty(returnUrl))
                    //{
                    //    return Results.Redirect("~/");
                    //}

                    //else
                    //{
                    //    // user might have clicked on a malicious link - should be logged
                    //    throw new Exception("invalid return URL");
                    //}
                }

                await events.RaiseAsync(new UserLoginFailureEvent(
                    username,
                    "invalid credentials",
                    clientId: context?.Client.ClientId));

                return Results.Redirect("~/");
                //return "Failed.";
            });

            return app;
        }
    }
}
