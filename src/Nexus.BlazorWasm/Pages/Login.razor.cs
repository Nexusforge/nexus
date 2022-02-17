using Nexus.Client;

namespace Nexus.BlazorWasm.Pages
{
    public partial class Login
    {
        private ICollection<AuthenticationSchemeDescription> _authenticationSchemes { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            var httpClient = new HttpClient();
            var client = new NexusOpenApiClient("https://localhost:8443", httpClient);

            _authenticationSchemes = await client.Users.GetAuthenticationSchemesAsync();
        }
    }
}
