using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI;
using Nexus.UI.Services;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

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
    .AddSingleton<TypeFaceService>()
    .AddSingleton(serviceProvider => (IJSInProcessRuntime)serviceProvider.GetRequiredService<IJSRuntime>())
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
