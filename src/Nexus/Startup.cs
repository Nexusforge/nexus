using MatBlazor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.ViewModels;
using NSwag.AspNetCore;
using Serilog;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
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
            var usersOptions = new UsersOptions();
            this.Configuration.GetSection(UsersOptions.Section).Bind(usersOptions);

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
                options.SignIn.RequireConfirmedAccount = usersOptions.VerifyEmail;
            });

            if (usersOptions.VerifyEmail)
                services.AddTransient<IEmailSender, EmailSender>();

            // blazor
            services.AddRazorPages();
            services.AddServerSideBlazor(options => options.DetailedErrors = true);

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

            /* not optimal */
#pragma warning disable ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'
            var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();
#pragma warning restore ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'

            foreach (var description in provider.ApiVersionDescriptions)
            {
                services.AddOpenApiDocument(config =>
                {
                    config.Title = "Nexus REST API";
                    config.Version = description.GroupName;
                    config.Description = "Explore resources and get their data." 
                        + (description.IsDeprecated ? " This API version is deprecated." : "");

                    config.ApiGroupNames = new[] { description.GroupName };
                    config.DocumentName = description.GroupName;
                });
            }

            // custom
#warning replace httpcontextaccessor by async authenticationStateProvider (https://github.com/dotnet/aspnetcore/issues/17585)
            services.AddHttpContextAccessor();

            services.AddTransient<DataService>();

            services.AddScoped<IUserIdService, UserIdService>();
            services.AddScoped<JwtService>();
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
            services.AddSingleton<JobService<ExportJob>>();

            services.Configure<GeneralOptions>(Configuration.GetSection(GeneralOptions.Section));
            services.Configure<PathsOptions>(Configuration.GetSection(PathsOptions.Section));
            services.Configure<SecurityOptions>(Configuration.GetSection(SecurityOptions.Section));
            services.Configure<ServerOptions>(Configuration.GetSection(ServerOptions.Section));
            services.Configure<SmtpOptions>(Configuration.GetSection(SmtpOptions.Section));
            services.Configure<UsersOptions>(Configuration.GetSection(UsersOptions.Section));
        }

        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              IServiceProvider serviceProvider,
                              IApiVersionDescriptionProvider provider,
                              IOptions<PathsOptions> pathsOptions)
        {
            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-6.0

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseForwardedHeaders();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseForwardedHeaders();
            }

            // blazor wasm
            app.UseBlazorFrameworkFiles("/vnext");
            app.UseStaticFiles();

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("vnext/{*path:nonfile}", "vnext/index.html");
            });

            //// static files
            //app.UseStaticFiles();

            //app.UseStaticFiles(new StaticFileOptions
            //{
            //    FileProvider = new LazyPhysicalFileProvider(pathsOptions.Value.Export),
            //    RequestPath = "/export"
            //});

            //// swagger
            //app.UseOpenApi();
            //app.UseSwaggerUi3(settings =>
            //{
            //    settings.Path = "/api";

            //    foreach (var description in provider.ApiVersionDescriptions)
            //    {
            //        settings.SwaggerRoutes.Add(new SwaggerUi3Route(description.GroupName.ToUpperInvariant(), $"/swagger/{description.GroupName}/swagger.json"));
            //    }
            //});

            //// Serilog Request Logging (https://andrewlock.net/using-serilog-aspnetcore-in-asp-net-core-3-reducing-log-verbosity/)
            //// LogContext properties are not included by default in request logging, workaround: https://nblumhardt.com/2019/10/serilog-mvc-logging/
            //app.UseSerilogRequestLogging();

            //// routing (for REST API)
            //app.UseRouting();

            //// default authentication
            //app.UseAuthentication();

            // custom authentication (to also authenticate via JWT bearer)
            //app.Use(async (context, next) =>
            //{
            //    bool terminate = false;

            //    if (!context.User.Identity.IsAuthenticated)
            //    {
            //        var authorizationHeader = context.Request.Headers["Authorization"];

            //        if (authorizationHeader.Any(header => header.StartsWith("Bearer")))
            //        {
            //            var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            //            if (result.Succeeded)
            //            {
            //                context.User = result.Principal;
            //            }
            //            else
            //            {
            //                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            //                var errorCode = result.Failure.Message.Split(':', count: 2).FirstOrDefault();

            //                var message = errorCode switch
            //                {
            //                    "IDX10230" => "Lifetime validation failed.",
            //                    "IDX10503" => "Signature validation failed.",
            //                    _ => "The bearer token could not be validated."
            //                };

            //                var bytes = Encoding.UTF8.GetBytes(message);
            //                await context.Response.Body.WriteAsync(bytes);
            //                terminate = true;
            //            }
            //        }
            //    }

            //    if (!terminate)
            //        await next();
            //});

            //// authorization
            //app.UseAuthorization();

            //// endpoints
            //app.UseEndpoints(endpoints =>
            //{
            //    endpoints.MapControllers();
            //    endpoints.MapBlazorHub();
            //    endpoints.MapFallbackToPage("/_Host");
            //});

            //// initialize app state
            //this.InitializeAppAsync(serviceProvider, pathsOptions.Value).Wait();
        }

        private async Task InitializeAppAsync(IServiceProvider serviceProvier, PathsOptions pathsOptions)
        {
            var appState = serviceProvier.GetRequiredService<AppState>();
            var appStateController = serviceProvier.GetRequiredService<AppStateController>();
            var userManagerWrapper = serviceProvier.GetRequiredService<IUserManagerWrapper>();
            var databaseManager = serviceProvier.GetRequiredService<IDatabaseManager>();

            // project
            if (databaseManager.TryReadProject(out var project))
            {
                appState.Project = JsonSerializer.Deserialize<NexusProject>(project);
            }
            else
            {
                appState.Project = new NexusProject(default);
            }

            // user manager
            await userManagerWrapper.InitializeAsync();

            // packages and catalogs
            await appStateController.ReloadCatalogsAsync(CancellationToken.None);
        }

        #endregion
    }
}
