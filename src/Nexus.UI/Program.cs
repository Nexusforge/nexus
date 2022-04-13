using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nexus.Api;
using Nexus.UI;
using Nexus.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var httpClient = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};

var nexusClient = new NexusClient(httpClient);

builder.Services
    .AddAuthorizationCore()
    .AddSingleton(nexusClient)
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
