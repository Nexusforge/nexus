using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using Nexus.Controllers;
using System.Reflection;

namespace Nexus.ClientGenerator
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var solutionRoot = args.Length >= 1
                ? args[0]
                : "../../../../../";

            var openApiFileName = args.Length == 2
                ? args[1]
                : "openapi.json";

            //
            var builder = WebApplication.CreateBuilder(args);

            builder.Services
                .AddMvcCore().AddApplicationPart(typeof(ArtifactsController).Assembly);

            builder.Services
                .AddRouting(options => options.LowercaseUrls = true);

            builder.Services
                .AddNexusOpenApi();

            var app = builder.Build();
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

            app.UseNexusOpenApi(provider, addExplorer: false);

            _ = app.RunAsync();

            // read open API document
            var client = new HttpClient();
            var response = await client.GetAsync("http://localhost:5000/openapi/v1/openapi.json");

            response.EnsureSuccessStatusCode();

            var openApiJsonString = await response.Content.ReadAsStringAsync();

            var document = new OpenApiStringReader()
                .Read(openApiJsonString, out var diagnostic);

            // client generation settings
            var settings = new GeneratorSettings(
                Namespace: "Nexus.Client",
                ClientName: "NexusClient",
                OutputFileName: "NexusClient",
                ExceptionType: "NexusException",
                NexusConfigurationHeaderKey: "Nexus-Configuration",
                AuthorizationHeaderKey: "Authorization");

            // generate C# client
            var basePath = Assembly.GetExecutingAssembly().Location;
            var csharpOutputPath = $"{solutionRoot}src/clients/cs-client/{settings.OutputFileName}.g.cs";
            var generator = new CSharpGenerator();
            var csharpCode = generator.Generate(document, settings);

            // save output
            File.WriteAllText(csharpOutputPath, csharpCode);

            // save open API document
            var openApiDocumentOutputPath = $"{solutionRoot}{openApiFileName}";
            File.WriteAllText(openApiDocumentOutputPath, openApiJsonString);
        }
    }
}