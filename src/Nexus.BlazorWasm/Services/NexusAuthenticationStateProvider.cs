﻿using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Client;
using System.Security.Claims;

namespace Nexus.BlazorWasm.Services
{
    public class NexusAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly NexusOpenApiClient _client;

        public NexusAuthenticationStateProvider(ClientProvider clientProvider)
        {
            _client = clientProvider.Client;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ClaimsIdentity identity;

            const string nameClaim = "name";

            try
            {
                var user = await _client.Users.GetMeAsync();

                var claims = user
                    .Claims
                    .Select(entry => new Claim(entry.Value.Type, entry.Value.Value))
                    .Concat(new[] { new Claim(nameClaim, user.Name) });

                identity = new ClaimsIdentity(
                    claims, 
                    authenticationType: user.Id.Split(new[] { '@' }, count: 2)[1],
                    nameType: nameClaim,
                    roleType: "role");
            }
            catch (Exception)
            {
                identity = new ClaimsIdentity();
            }

            var principal = new ClaimsPrincipal(identity);
            
            return new AuthenticationState(principal);
        }
    }
}