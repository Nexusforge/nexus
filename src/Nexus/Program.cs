using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Nexus.Core;
using Nexus.Services;
using Serilog;
using System.Globalization;
using System.Text.Json;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// culture
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// configuration
var configuration = NexusOptionsBase.BuildConfiguration(args);

var generalOptions = configuration
    .GetSection(GeneralOptions.Section)
    .Get<GeneralOptions>();

var serverOptions = configuration
    .GetSection(ServerOptions.Section)
    .Get<ServerOptions>();

var securityOptions = configuration
    .GetSection(SecurityOptions.Section)
    .Get<SecurityOptions>();

var pathsOptions = configuration
    .GetSection(PathsOptions.Section)
    .Get<PathsOptions>();

// logging (https://nblumhardt.com/2019/10/serilog-in-aspnetcore-3/)
var applicationName = generalOptions.ApplicationName;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.WithProperty("ApplicationName", applicationName)
    .CreateLogger();

// checks
if (!securityOptions.OidcProviders.Any())
    Log.Warning("No OpenID Connect providers configured");

// run
try
{
    Log.Information("Start host");

    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddConfiguration(configuration);
    builder.Host.UseSerilog();

    // Add services to the container.
    AddServices(builder.Services, configuration, pathsOptions, securityOptions);

    // Build 
    var app = builder.Build();

    // Configure the HTTP request pipeline.
    ConfigurePipeline(app);

    // initialize app state
    await InitializeAppAsync(app.Services, pathsOptions, securityOptions, app.Logger);

    // Run
    var baseUrl = $"{serverOptions.HttpScheme}://{serverOptions.HttpAddress}:{serverOptions.HttpPort}";
    app.Run(baseUrl);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

void AddServices(
    IServiceCollection services, 
    IConfiguration configuration, 
    PathsOptions pathsOptions,
    SecurityOptions securityOptions)
{
    // database
    var filePath = Path.Combine(pathsOptions.Config, "users.db");

    services.AddDbContext<UserDbContext>(
        options => options.UseSqlite($"Data Source={filePath}"));

    // forwarded headers
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.All;
#warning replace this with proper external configuration
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // authentication
    services.AddNexusAuthentication(securityOptions);

    // blazor
    services.AddRazorPages();

    // authorization
    services.AddAuthorization(options =>
    {
        options.AddPolicy(Policies.RequireAdmin, policy => policy.RequireClaim(Claims.IS_ADMIN, "true"));
    });

    // Open API
    services.AddNexusOpenApi();

    // development identity provider
    services.AddNexusIdentityProvider();

    // routing
    services.AddRouting(options => options.LowercaseUrls = true);

    // HTTP context
    services.AddHttpContextAccessor();

    // custom
    services.AddTransient<IDataService, DataService>();

    services.AddScoped<IDBService, DbService>();
    services.AddScoped<INexusAuthenticationService, NexusAuthenticationService>();

    services.AddSingleton<AppState>();
    services.AddSingleton<AppStateManager>();
    services.AddSingleton<IJobService, JobService>();
    services.AddSingleton<IDataControllerService, DataControllerService>();
    services.AddSingleton<ICatalogManager, CatalogManager>();
    services.AddSingleton<IDatabaseManager, DatabaseManager>();
    services.AddSingleton<IExtensionHive, ExtensionHive>();
    services.AddSingleton<IUserManagerWrapper, UserManagerWrapper>();

    services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.Section));
    services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.Section));
    services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.Section));
    services.Configure<ServerOptions>(configuration.GetSection(ServerOptions.Section));
    services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
}

void ConfigurePipeline(WebApplication app)
{
    // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-6.0

    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }
    else
    {
#warning write error page HTML here without razor page (example: app.UseExceptionHandler)

        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    // blazor wasm
    app.UseBlazorFrameworkFiles();

    // static files
    app.UseStaticFiles();

    // Open API
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    app.UseNexusOpenApi(provider, addExplorer: true);

    // Serilog Request Logging (https://andrewlock.net/using-serilog-aspnetcore-in-asp-net-core-3-reducing-log-verbosity/)
    // LogContext properties are not included by default in request logging, workaround: https://nblumhardt.com/2019/10/serilog-mvc-logging/
    app.UseSerilogRequestLogging();

    // routing (for REST API)
    app.UseRouting();

    // development identity provider
    app.UseNexusIdentityProvider();

    // default authentication
    app.UseAuthentication();

    // authorization
    app.UseAuthorization();

    // endpoints
    app.MapControllers();
    app.MapFallbackToFile("index.html");
}

async Task InitializeAppAsync(
    IServiceProvider serviceProvider,
    PathsOptions pathsOptions,
    SecurityOptions securityOptions,
    ILogger logger)
{
    var appState = serviceProvider.GetRequiredService<AppState>();
    var appStateManager = serviceProvider.GetRequiredService<AppStateManager>();
    var databaseManager = serviceProvider.GetRequiredService<IDatabaseManager>();

    // database
    using var scope = serviceProvider.CreateScope();
    var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

    await userContext.Database.EnsureCreatedAsync();

    // project
    if (databaseManager.TryReadProject(out var project))
        appState.Project = JsonSerializer.Deserialize<NexusProject>(project) ?? throw new Exception("project is null");
    
    else
        appState.Project = new NexusProject(
            new Dictionary<Guid, PackageReference>(),
            new Dictionary<string, UserConfiguration>());

    // packages and catalogs
    await appStateManager.LoadPackagesAsync(new Progress<double>(), CancellationToken.None);
}