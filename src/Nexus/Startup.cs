using MatBlazor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using Nexus.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Nexus
{
    public class Startup
    {
        #region Properties

        public static SymmetricSecurityKey SecurityKey { get; } = new SymmetricSecurityKey(Guid.NewGuid().ToByteArray());

        #endregion

        #region Methods

        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services, IOptions<UsersOptions> usersOptions)
        {
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

            // identity (customize: https://docs.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-3.1)
            services.AddDefaultIdentity<IdentityUser>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            services.Configure<IdentityOptions>(options =>
            {
                // Instead of RequireConfirmedEmail, this one has the desired effect!
                options.SignIn.RequireConfirmedAccount = usersOptions.Value.VerifyEmail;
            });

            if (usersOptions.Value.VerifyEmail)
                services.AddTransient<IEmailSender, EmailSender>();

            // blazor
            services.AddRazorPages();
            services.AddServerSideBlazor();

            // matblazor
            services.AddMatToaster(config =>
            {
                config.Position = MatToastPosition.BottomCenter;
                config.PreventDuplicates = true;
                config.NewestOnTop = true;
                config.ShowCloseButton = true;
                config.MaximumOpacity = 95;
                config.VisibleStateDuration = 10000;
            });

            // authentication
            services.AddAuthentication(options =>
            {
                // Identity made Cookie authentication the default.
                // Custom authentication needed (see app.Use(...) below).
            })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters()
                    {
                        LifetimeValidator = (before, expires, token, parameters) => expires > DateTime.UtcNow,
                        ValidateAudience = false,
                        ValidateIssuer = false,
                        ValidateActor = false,
                        ValidateLifetime = true,
                        IssuerSigningKey = Startup.SecurityKey
                    };

                    options.Events = new JwtBearerEvents()
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];

                            if (!string.IsNullOrEmpty(accessToken) && context.Request.Headers["Upgrade"] == "websocket")
                                context.Token = accessToken;

                            return Task.CompletedTask;
                        }
                    };
                });

            // authorization
            services.AddAuthorization(options =>
            {
                options.AddPolicy("RequireAdmin", policy => policy.RequireClaim(Claims.IS_ADMIN, "true"));
            });

            // swagger
            services.AddControllers()
                .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            services.AddSwaggerDocument(config =>
            {
                config.Title = "Nexus REST API";
                config.Version = "v1";
                config.Description = "Explore resources and get their data.";
                config.DocumentName = "v1";
                //config.OperationProcessors.Add(new OperationSecurityScopeProcessor("JWT Token"));
                //config.AddSecurity("JWT Token", Enumerable.Empty<string>(),
                //    new OpenApiSecurityScheme()
                //    {
                //        Type = OpenApiSecuritySchemeType.ApiKey,
                //        Name = "Authorization",
                //        In = OpenApiSecurityApiKeyLocation.Header,
                //        Description = "Copy this into the value field: Bearer {token}"
                //    }
                //);
            });

            // custom
#warning replace httpcontextaccessor by async authenticationStateProvider (https://github.com/dotnet/aspnetcore/issues/17585)
            services.AddHttpContextAccessor();

            services.AddScoped<MonacoService>();
            services.AddScoped<IUserIdService, UserIdService>();
            services.AddScoped<UserState>();
            services.AddScoped<SettingsViewModel>();
            services.AddScoped<ToasterService>();
            services.AddScoped<JwtService<IdentityUser>>();
            services.AddScoped<JobEditor>();

            services.AddTransient<DataService>();
            services.AddTransient<AggregationService>();
            services.AddTransient<IDataControllerService, DataControllerService>();

            services.AddSingleton<ExtensionHive>();
            services.AddSingleton<AppState>();
            services.AddSingleton<IFileAccessManager, FileAccessManager>();
            services.AddSingleton<JobService<ExportJob>>();
            services.AddSingleton<JobService<AggregationJob>>();
            services.AddSingleton<IDatabaseManager, DatabaseManager>();
            services.AddSingleton<UserManager>();

            services.Configure<GeneralOptions>(Configuration.GetSection(GeneralOptions.Section));
            services.Configure<ServerOptions>(Configuration.GetSection(ServerOptions.Section));
            services.Configure<PathsOptions>(Configuration.GetSection(PathsOptions.Section));
            services.Configure<SecurityOptions>(Configuration.GetSection(SecurityOptions.Section));
            services.Configure<UsersOptions>(Configuration.GetSection(UsersOptions.Section));
            services.Configure<SmtpOptions>(Configuration.GetSection(SmtpOptions.Section));
        }

        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              IServiceProvider serviceProvider,
                              IOptions<PathsOptions> pathOptions)
        {
            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-5.0

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseForwardedHeaders();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseForwardedHeaders();
            }

            // static files
            app.UseStaticFiles();

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new LazyPhysicalFileProvider(pathOptions.Value.Data, "ATTACHMENTS"),
                RequestPath = "/attachments",
                ServeUnknownFileTypes = true
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(Assembly.GetExecutingAssembly().Location, "..", "Connectors")),
                RequestPath = "/connectors",
                ServeUnknownFileTypes = true
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new LazyPhysicalFileProvider(pathOptions.Value.Data, "EXPORT"),
                RequestPath = "/export"
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new LazyPhysicalFileProvider(pathOptions.Value.Data, "PRESETS"),
                RequestPath = "/presets"
            });

            // swagger
            app.UseOpenApi();
            app.UseSwaggerUi3(configure => configure.SwaggerRoutes);

            // routing (for REST API)
            app.UseRouting();

            // default authentication
            app.UseAuthentication();

            // custom authentication (to also authenticate via JWT bearer)
            app.Use(async (context, next) =>
            {
                bool terminate = false;

                if (!context.User.Identity.IsAuthenticated)
                {
                    var authorizationHeader = context.Request.Headers["Authorization"];
                    
                    if (authorizationHeader.Any(header => header.StartsWith("Bearer")))
                    {
                        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

                        if (result.Succeeded)
                        {
                            context.User = result.Principal;
                        }
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                            var errorCode = result.Failure.Message.Split(':', count: 2).FirstOrDefault();

                            var message = errorCode switch
                            {
                                "IDX10230" => "Lifetime validation failed.",
                                "IDX10503" => "Signature validation failed.",
                                _ => "The bearer token could not be validated."
                            };

                            var bytes = Encoding.UTF8.GetBytes(message);
                            await context.Response.Body.WriteAsync(bytes);
                            terminate = true;
                        }
                    }
                }

                if (!terminate)
                    await next();
            });

            // authorization
            app.UseAuthorization();

            // endpoints
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });

            // initialize app state
            await this.InitializeAppAsync(serviceProvider);
        }

        private async Task InitializeAppAsync(IServiceProvider serviceProvier)
        {
            var appState = serviceProvier.GetRequiredService<AppState>();
            var databaseManager = serviceProvier.GetRequiredService<IDatabaseManager>();
            var userManager = serviceProvier.GetRequiredService<UserManager>();
            var extensionHive = serviceProvier.GetRequiredService<ExtensionHive>();
            var pathsOptions = serviceProvier.GetRequiredService<IOptions<PathsOptions>>().Value;

            // package references
            await extensionHive.LoadPackagesAsync(packageReferences, cancellationToken);

            // news
            if (databaseManager.TryReadNews(out var stream))
            {
                var jsonString = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
                appState.NewsPaper = JsonSerializerHelper.Deserialize<NewsPaper>(jsonString);
            }
            else
            {
                appState.NewsPaper = new NewsPaper();
            }

            // filters
            var filterSettingsFilePath = Path.Combine(pathOptions.Config, "filters.json");
            appState.FilterSettings = new FilterSettingsViewModel(filterSettingsFilePath);
            this.InitializeFilterSettings(appState.FilterSettings.Model, filterSettingsFilePath);

#error not yet clean
            // ??
            _resourceCache = new Dictionary<CatalogContainer, List<ResourceViewModel>>();
            _updateDatabaseSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

            // project
            await this.InitializeProjectAsync(appState, databaseManager, pathsOptions);

            // user manager
            await userManager.InitializeAsync();

            // 
            _ = this.UpdateDatabaseAsync();
        }

        private async Task InitializeProjectAsync(AppState appState, IDatabaseManager databaseManager, PathsOptions pathsOptions)
        {
            if (databaseManager.TryReadProject(out var stream))
            {
                var jsonString = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
                appState.Project = JsonSerializerHelper.Deserialize<NexusProject>(jsonString);
            }
            else
            {
                appState.Project = new NexusProject();
            }

            // extend config with more data readers
            var inmemoryBackendSource = new BackendSource()
            {
                Type = "Nexus.Builtin.Inmemory",
                ResourceLocator = new Uri("memory://localhost")
            };

            if (!appState.Project.BackendSources.Contains(inmemoryBackendSource))
                appState.Project.BackendSources.Add(inmemoryBackendSource);

            var filterBackendSource = new BackendSource()
            {
                Type = "Nexus.Builtin.Filters",
                ResourceLocator = new Uri(pathsOptions.Cache, UriKind.RelativeOrAbsolute)
            };

            if (!appState.Project.BackendSources.Contains(filterBackendSource))
                appState.Project.BackendSources.Add(filterBackendSource);
        }

        private void InitializeFilterSettings(FilterSettings filterSettings, string filePath)
        {
            // ensure that code samples of test user are present
            var testCodes = filterSettings.CodeDefinitions.Where(code => code.Owner == "test@nexus.org");

            if (!testCodes.Any(testCode => testCode.Name == "Simple filter (C#)"))
            {
                using var streamReader1 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.TestUserFilterCodeTemplateSimple.cs"));

                filterSettings.CodeDefinitions.Add(new CodeDefinition()
                {
                    Code = streamReader1.ReadToEnd(),
                    CodeLanguage = CodeLanguage.CSharp,
                    CodeType = CodeType.Filter,
                    CreationDate = DateTime.UtcNow,
                    IsEnabled = true,
                    Name = "Simple filter (C#)",
                    Owner = "test@nexus.org",
                    RequestedCatalogIds = new List<string>() { "/IN_MEMORY/TEST/ACCESSIBLE" },
                    SampleRate = "1 s"
                });

                filterSettings.Save(filePath);
            }

            if (!testCodes.Any(testCode => testCode.Name == "Simple shared (C#)"))
            {
                using var streamReader2 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.TestUserSharedCodeTemplateSimple.cs"));

                filterSettings.CodeDefinitions.Add(new CodeDefinition()
                {
                    Code = streamReader2.ReadToEnd(),
                    CodeLanguage = CodeLanguage.CSharp,
                    CodeType = CodeType.Shared,
                    CreationDate = DateTime.UtcNow,
                    IsEnabled = true,
                    Name = "Simple shared (C#)",
                    Owner = "test@nexus.org",
                    RequestedCatalogIds = new List<string>(),
                    SampleRate = string.Empty
                });

                filterSettings.Save(filePath);
            }
        }

        #endregion
    }
}
