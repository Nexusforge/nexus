using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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

// logging (https://nblumhardt.com/2019/10/serilog-in-aspnetcore-3/)
var applicationName = generalOptions.ApplicationName;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.WithProperty("ApplicationName", applicationName)
    .CreateLogger();

// run
try
{
    Log.Information("Start host.");

    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddConfiguration(configuration);
    builder.Host.UseSerilog();

    // Add services to the container.
    AddServices(builder.Services, configuration);

    // Build 
    var app = builder.Build();

    // Configure the HTTP request pipeline.
    ConfigurePipeline(app);

    // initialize app state
    var pathsOptions = app.Services.GetRequiredService<IOptions<PathsOptions>>();
    var securityOptions = app.Services.GetRequiredService<IOptions<SecurityOptions>>();
    await InitializeAppAsync(app.Services, pathsOptions.Value, securityOptions.Value, app.Logger);

    // Run
    var baseUrl = $"{serverOptions.HttpScheme}://{serverOptions.HttpAddress}:{serverOptions.HttpPort}";
    app.Run(baseUrl);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

void AddServices(IServiceCollection services, IConfiguration configuration)
{
    var securityOptions = configuration
        .GetSection(SecurityOptions.Section)
        .Get<SecurityOptions>();

    // database
    services.AddDbContext<ApplicationDbContext>();

    // forwarded headers
    services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.All;
#warning replace this with proper external configuration
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // authentication
    services
        .AddAuthentication(options =>
        {
            // Very important, because AddIdentity made Cookie Authentication the default.
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters()
            {
                ClockSkew = TimeSpan.Zero,
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateActor = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(securityOptions.Base64JwtSigningKey))
            };
        });

    // blazor
    services.AddRazorPages();

    // authorization
    services.AddAuthorization(options =>
    {
        options.AddPolicy(Policies.RequireAdmin, policy => policy.RequireClaim(Claims.IS_ADMIN, "true"));
    });

    // Open API
    services.AddNexusOpenApi();

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
#warning Ooops. Something went wrong ;-)
        app.UseExceptionHandler("/Error");

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

    // default authentication
    app.UseAuthentication();

    // authorization
    app.UseAuthorization();

    // endpoints
    app.MapRazorPages();
    app.MapControllers();
    app.MapFallbackToFile("index.html");
}

async Task InitializeAppAsync(
    IServiceProvider serviceProvier,
    PathsOptions pathsOptions,
    SecurityOptions securityOptions,
    ILogger logger)
{
    var appState = serviceProvier.GetRequiredService<AppState>();
    var appStateManager = serviceProvier.GetRequiredService<AppStateManager>();
    var databaseManager = serviceProvier.GetRequiredService<IDatabaseManager>();

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