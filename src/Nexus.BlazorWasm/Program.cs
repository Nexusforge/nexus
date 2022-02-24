using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nexus.BlazorWasm;
using Nexus.BlazorWasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var httpClient = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};

builder.Services
    .AddAuthorizationCore()
    .AddScoped(sp => httpClient)
    .AddScoped<ClientProvider>()
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
