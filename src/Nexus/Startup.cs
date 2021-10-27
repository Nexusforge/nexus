using MatBlazor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using Nexus.ViewModels;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus
{
    internal class Startup
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

        public void ConfigureServices(IServiceCollection services)
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

#warning Repair this!
            services.Configure<IdentityOptions>(options =>
            {
                // Instead of RequireConfirmedEmail, this one has the desired effect!
                //options.SignIn.RequireConfirmedAccount = usersOptions.Value.VerifyEmail;
            });

            //if (usersOptions.Value.VerifyEmail)
            //    services.AddTransient<IEmailSender, EmailSender>();

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

            services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

            services.AddSwaggerGen(
                options =>
                {
                    //options.OperationFilter<SwaggerDefaultValues>();
                    options.IncludeXmlComments(Path.ChangeExtension(typeof(Startup).Assembly.Location, "xml"));
                });

            // custom
#warning replace httpcontextaccessor by async authenticationStateProvider (https://github.com/dotnet/aspnetcore/issues/17585)
            services.AddHttpContextAccessor();

            services.AddTransient<AggregationService>();
            services.AddTransient<DataService>();

            services.AddScoped<IUserIdService, UserIdService>();
            services.AddScoped<JobEditor>();
            services.AddScoped<JwtService>();
            services.AddScoped<MonacoService>();
            services.AddScoped<SettingsViewModel>();
            services.AddScoped<ToasterService>();
            services.AddScoped<UserState>();

            services.AddSingleton<AppState>();
            services.AddSingleton<AppStateController>();
            services.AddSingleton<IDataControllerService, DataControllerService>();
            services.AddSingleton<ICatalogManager, CatalogManager>();
            services.AddSingleton<IDatabaseManager, DatabaseManager>();
            services.AddSingleton<IExtensionHive, ExtensionHive>();
            services.AddSingleton<IUserManagerWrapper, UserManagerWrapper>();
            services.AddSingleton<JobService<AggregationJob>>();
            services.AddSingleton<JobService<ExportJob>>();

            services.Configure<GeneralOptions>(Configuration.GetSection(GeneralOptions.Section));
            services.Configure<PathsOptions>(Configuration.GetSection(PathsOptions.Section));
            services.Configure<SecurityOptions>(Configuration.GetSection(SecurityOptions.Section));
            services.Configure<ServerOptions>(Configuration.GetSection(ServerOptions.Section));
            services.Configure<SmtpOptions>(Configuration.GetSection(SmtpOptions.Section));
#warning Repair this!
            //services.Configure<UsersOptions>(Configuration.GetSection(UsersOptions.Section));
        }

        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              IServiceProvider serviceProvider,
                              IApiVersionDescriptionProvider provider,
                              IOptions<PathsOptions> pathsOptions)
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
                FileProvider = new LazyPhysicalFileProvider(pathsOptions.Value.Catalogs),
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
                FileProvider = new LazyPhysicalFileProvider(pathsOptions.Value.Export),
                RequestPath = "/export"
            });

            // swagger
            app.UseSwagger();
            app.UseSwaggerUI(
                options =>
                {
                    options.RoutePrefix = "api";

                    foreach (var description in provider.ApiVersionDescriptions)
                    {
                        options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
                    }
                });

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
            this.InitializeAppAsync(serviceProvider, pathsOptions.Value).Wait();
        }

        private async Task InitializeAppAsync(IServiceProvider serviceProvier, PathsOptions pathsOptions)
        {
            var appState = serviceProvier.GetRequiredService<AppState>();
            var appStateController = serviceProvier.GetRequiredService<AppStateController>();
            var userManagerWrapper = serviceProvier.GetRequiredService<IUserManagerWrapper>();
            var databaseManager = serviceProvier.GetRequiredService<IDatabaseManager>();

            // project
            if (databaseManager.TryReadProject(out var stream1))
            {
                var jsonString = await new StreamReader(stream1, Encoding.UTF8).ReadToEndAsync();
                appState.Project = JsonSerializerHelper.Deserialize<NexusProject>(jsonString);
            }
            else
            {
                appState.Project = new NexusProject(default, default);
            }

            // news
            if (databaseManager.TryReadNews(out var stream2))
            {
                var jsonString = await new StreamReader(stream2, Encoding.UTF8).ReadToEndAsync();
                appState.NewsPaper = JsonSerializerHelper.Deserialize<NewsPaper>(jsonString);
            }
            else
            {
                appState.NewsPaper = new NewsPaper();
            }

            // filters
            var filterSettingsFilePath = Path.Combine(pathsOptions.Config, "filters.json");
            appState.FilterSettings = new FilterSettingsViewModel(filterSettingsFilePath);
            this.InitializeFilterSettings(appState.FilterSettings.Model, filterSettingsFilePath);

            // user manager
            await userManagerWrapper.InitializeAsync();

            // packages and catalogs
            _ = appStateController.ReloadCatalogsAsync(CancellationToken.None);
        }

        private void InitializeFilterSettings(FilterSettings filterSettings, string filePath)
        {
            // ensure that code samples of test user are present
            var testCodes = filterSettings.CodeDefinitions.Where(code => code.Owner == "test@nexus.org");

            if (!testCodes.Any(testCode => testCode.Name == "Simple filter (C#)"))
            {
                using var streamReader1 = new StreamReader(ResourceLoader.GetResourceStream("Resources.TestUserFilterCodeTemplateSimple.cs", addRootNamespace: true));

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
                    SamplePeriod = TimeSpan.FromSeconds(1)
                });

                var jsonString = JsonSerializerHelper.Serialize(filterSettings);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, jsonString);
            }

            if (!testCodes.Any(testCode => testCode.Name == "Simple shared (C#)"))
            {
                using var streamReader2 = new StreamReader(ResourceLoader.GetResourceStream("Resources.TestUserSharedCodeTemplateSimple.cs", addRootNamespace: true));

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
                    SamplePeriod = default
                });

                var jsonString = JsonSerializerHelper.Serialize(filterSettings);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, jsonString);
            }
        }

        #endregion
    }
}
