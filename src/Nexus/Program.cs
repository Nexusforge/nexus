using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.Services;
using NJsonSchema.Generation;
using NSwag;
using NSwag.AspNetCore;
using Serilog;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    await ConfigurePipelineAsync(app);

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

    // identity
    services
        .Configure<IdentityOptions>(configuration.GetSection(NexusOptionsBase.IdentitySection));

    services
        .AddIdentityCore<NexusUser>()
        .AddSignInManager<SignInManager<NexusUser>>()
        .AddDefaultTokenProviders()
        .AddEntityFrameworkStores<ApplicationDbContext>();

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

    // email sender
    services.AddTransient<IEmailSender, EmailSender>();

    // blazor
    services.AddRazorPages();

    // authorization
    services.AddAuthorization(options =>
    {
        options.AddPolicy(Policies.RequireAdmin, policy => policy.RequireClaim(Claims.IS_ADMIN, "true"));
    });

    // swagger (https://github.com/dotnet/aspnet-api-versioning/tree/master/samples/aspnetcore/SwaggerSample)
    services.AddControllers()
        .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .ConfigureApplicationPartManager(
            manager =>
            {
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

    services.AddApiVersioning(
        options =>
        {
            options.ReportApiVersions = true;
        });

    services.AddVersionedApiExplorer(
        options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

    // routing
    services.AddRouting(options => options.LowercaseUrls = true);

    /* not optimal */
#pragma warning disable ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'
    var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();
#pragma warning restore ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'

    foreach (var description in provider.ApiVersionDescriptions)
    {
        services.AddOpenApiDocument(config =>
        {
            config.DefaultReferenceTypeNullHandling = ReferenceTypeNullHandling.NotNull;

            config.Title = "Nexus REST API";
            config.Version = description.GroupName;
            config.Description = "Explore resources and get their data."
                + (description.IsDeprecated ? " This API version is deprecated." : "");

            config.ApiGroupNames = new[] { description.GroupName };
            config.DocumentName = description.GroupName;

            config.AddSecurity("JWT", Enumerable.Empty<string>(), new OpenApiSecurityScheme()
            {
                Type = OpenApiSecuritySchemeType.ApiKey,
                Name = "Authorization",
                In = OpenApiSecurityApiKeyLocation.Header,
                Description = "Please enter 'Bearer {your JWT token}'"
            });
        });
    }

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

Task ConfigurePipelineAsync(WebApplication app)
{
    var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    var pathsOptions = app.Services.GetRequiredService<IOptions<PathsOptions>>();
    var securityOptions = app.Services.GetRequiredService<IOptions<SecurityOptions>>();

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

    Directory.CreateDirectory(pathsOptions.Value.Artifacts);

    app.UseStaticFiles("/export");

    // swagger
    app.UseOpenApi();
    app.UseSwaggerUi3(settings =>
    {
        settings.Path = "/api";

        foreach (var description in provider.ApiVersionDescriptions)
        {
            settings.SwaggerRoutes.Add(new SwaggerUi3Route(description.GroupName.ToUpperInvariant(), $"/swagger/{description.GroupName}/swagger.json"));
        }
    });

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

    // initialize app state
    return InitializeAppAsync(app.Services, pathsOptions.Value, securityOptions.Value, app.Logger);
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

    // user manager
    await InitializeDatabaseAsync(serviceProvier, pathsOptions, securityOptions, logger);

    // packages and catalogs
    await appStateManager.LoadPackagesAsync(new Progress<double>(), CancellationToken.None);
}

async Task InitializeDatabaseAsync(
    IServiceProvider serviceProvider,
    PathsOptions pathsOptions,
    SecurityOptions securityOptions,
    ILogger logger)
{
    using (var scope = serviceProvider.CreateScope())
    {
        var userDB = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<NexusUser>>();

        // database
        Directory.CreateDirectory(pathsOptions.Config);

        if (userDB.Database.EnsureCreated())
            logger.LogInformation("SQLite database initialized");

        // ensure there is a root user
        var rootUsername = securityOptions.RootUser;
        var rootPassword = securityOptions.RootPassword;

        if ((await userManager.FindByNameAsync(rootUsername)) is null)
        {
            var user = new NexusUser(rootUsername);
            var result = await userManager.CreateAsync(user, rootPassword);

            if (result.Succeeded)
            {
                // confirm account
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await userManager.ConfirmEmailAsync(user, token);

                // add claim
                var claim = new Claim(Claims.IS_ADMIN, "true");
                await userManager.AddClaimAsync(user, claim);

                // remove default root user
                if (rootUsername != SecurityOptions.DefaultRootUser)
                {
                    var userToDelete = await userManager.FindByNameAsync(SecurityOptions.DefaultRootUser);

                    if (userToDelete is not null)
                        await userManager.DeleteAsync(userToDelete);
                }
            }
            else
            {
                await userManager.CreateAsync(
                    new NexusUser(SecurityOptions.DefaultRootUser), SecurityOptions.DefaultRootPassword);
            }
        }

        // ensure there is a test user
        var defaultTestUsername = "test@nexus.localhost";
        var defaultTestPassword = "#test0/User1";

        if ((await userManager.FindByNameAsync(defaultTestUsername)) is null)
        {
            var user = new NexusUser(defaultTestUsername);
            var result = await userManager.CreateAsync(user, defaultTestPassword);

            if (result.Succeeded)
            {
                // confirm account
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await userManager.ConfirmEmailAsync(user, token);

                // add claim
                var claim = new Claim(Claims.CAN_ACCESS_CATALOG, "/IN_MEMORY/TEST/ACCESSIBLE");
                await userManager.AddClaimAsync(user, claim);
            }
        }
    }
}