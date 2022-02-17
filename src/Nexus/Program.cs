using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Nexus.Core;
using Nexus.Services;
using Serilog;
using System.Globalization;
using System.Security.Claims;
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
    Log.Warning("No OpenID Connect provider configured");

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
    if (securityOptions.OidcProviders.Any())
    {
        var builder = services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = securityOptions.OidcProviders.First().Scheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
            //.AddJwtBearer(options =>
            //{
            //    options.TokenValidationParameters = new TokenValidationParameters()
            //    {
            //        ClockSkew = TimeSpan.Zero,
            //        ValidateAudience = false,
            //        ValidateIssuer = false,
            //        ValidateActor = false,
            //        ValidateLifetime = true,
            //        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(securityOptions.Base64JwtSigningKey))
            //    };
            //});

        foreach (var provider in securityOptions.OidcProviders)
        {
            builder.AddOpenIdConnect(provider.Scheme, provider.DisplayName, options =>
            {
                options.Authority = provider.Authority;
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;

                options.CallbackPath = $"/signin-oidc/{provider.Scheme}";
                options.ResponseType = OpenIdConnectResponseType.Code;

                var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                if (environmentName == "Development")
                    options.RequireHttpsMetadata = false;

                options.Events = new OpenIdConnectEvents()
                {
                    OnTokenValidated = async context =>
                    {
                        // scopes
                        // https://openid.net/specs/openid-connect-basic-1_0.html#Scopes

                        // sub claim type will be mapped to ClaimTypes.NameIdentifier
                        // https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/6e7a53e241e4566998d3bf365f03acd0da699a31/src/System.IdentityModel.Tokens.Jwt/ClaimTypeMapping.cs#L59

                        var principal = context.Principal;

                        if (principal is null)
                            throw new Exception("The principal is null. This should never happen.");

                        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) 
                            ?? throw new Exception("The name identifier claim is missing. This should never happen.");

                        var userName = principal.FindFirstValue(ClaimTypes.Name)
                            ?? throw new Exception("The name claim is missing.");

                        var userContext = context.HttpContext.RequestServices.GetRequiredService<UserDbContext>();
                        
                        var user = await userContext.Users
                            .Include(user => user.Claims)
                            .SingleOrDefaultAsync(user => 
                                user.Id == userId && 
                                user.Scheme == context.Scheme.Name);

                        if (user is null)
                        {
                            user = new NexusUser()
                            {
                                Id = userId,
                                Name = userName,
                                Scheme = context.Scheme.Name,
                                Claims = new List<NexusClaim>(),
                                RefreshTokens = new List<RefreshToken>()
                            };

                            var isFirstUser = !userContext.Users.Any();

                            if (isFirstUser)
                            {
                                user.Claims.Add(new NexusClaim()
                                {
                                    Type = Claims.IS_ADMIN,
                                    Value = "true"
                                });
                            }

                            userContext.Users.Add(user);
                        }

                        else
                        {
                            // user name may change, so update it
                            user.Name = userName;
                        }

                        await userContext.SaveChangesAsync();

                        var appIdentity = new ClaimsIdentity(user.Claims.Select(claim => new Claim(claim.Type, claim.Value)));
                        principal.AddIdentity(appIdentity);
                    }
                };
            });
        }
    }

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