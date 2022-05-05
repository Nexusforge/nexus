using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI;
using Nexus.UI.Core;
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

var client = new NexusClient(httpClient);
var authenticationSchemes = await client.Users.GetAuthenticationSchemesAsync();

builder.Services
    .AddAuthorizationCore()
    .AddSingleton<INexusClient>(client)
    .AddSingleton<IJSInProcessRuntime>(serviceProvider => (IJSInProcessRuntime)serviceProvider.GetRequiredService<IJSRuntime>())
    .AddSingleton<IAppState>(serviceProvider => 
    {
        var jSInProcessRuntime = serviceProvider.GetRequiredService<IJSInProcessRuntime>();
        var appState = new AppState(authenticationSchemes, (INexusClient)client, jSInProcessRuntime);

        return appState;
    })
    .AddSingleton<TypeFaceService>()
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
