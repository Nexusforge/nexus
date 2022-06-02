using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Api;
using System.Security.Claims;

namespace Nexus.UI.Services
{
    public class NexusAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly INexusClient _client;

        public NexusAuthenticationStateProvider(INexusClient client)
        {
            _client = client;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ClaimsIdentity identity;

            const string NAME_CLAIM = "name";
            const string ROLE_CLAIM = "role";

            try
            {
                var user = await _client.Users.GetMeAsync();

                var claims = user
                    .Claims
                    .Select(entry => new Claim(entry.Value.Type, entry.Value.Value))
                    .Concat(new[] { new Claim(NAME_CLAIM, user.Name) });

                identity = new ClaimsIdentity(
                    claims, 
                    authenticationType: user.Id.Split(new[] { '@' }, count: 2)[1],
                    nameType: NAME_CLAIM,
                    roleType: ROLE_CLAIM);
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
