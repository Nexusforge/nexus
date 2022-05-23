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

#warning Very large attachment upload is first loading into memory and only then to server (https://stackoverflow.com/questions/66770670/streaming-large-files-from-blazor-webassembly)

var httpClient = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};

var client = new NexusClient(httpClient);
var authenticationSchemes = await client.Users.GetAuthenticationSchemesAsync();

builder.Services
    .AddAuthorizationCore()
    .AddSingleton<ToastService>()
    .AddSingleton<INexusClient>(client)
    .AddSingleton<IJSInProcessRuntime>(serviceProvider => (IJSInProcessRuntime)serviceProvider.GetRequiredService<IJSRuntime>())
    .AddSingleton<IAppState>(serviceProvider => 
    {
        var jsRuntime = serviceProvider.GetRequiredService<IJSInProcessRuntime>();
        var toastService = serviceProvider.GetRequiredService<ToastService>();
        var appState = new AppState(authenticationSchemes, (INexusClient)client, jsRuntime, toastService);

        return appState;
    })
    .AddSingleton<TypeFaceService>()
    .AddScoped<AuthenticationStateProvider, NexusAuthenticationStateProvider>();

await builder.Build().RunAsync();
