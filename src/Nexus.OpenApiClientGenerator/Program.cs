using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using Nexus.Controllers;

namespace Nexus.OpenApiClientGenerator
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
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
                ClientName: "NexusOpenApiClient",
                OutputFileName: "NexusOpenApi",
                ExceptionType: "NexusApiException",
                NexusConfigurationHeaderKey: "Nexus-Configuration",
                AuthorizationHeaderKey: "Authorization");

            // generate C# client
            var csharpOutputPath = $"../../../../../src/Nexus.OpenApiClient/{settings.OutputFileName}.g.cs";
            var generator = new CSharpGenerator();
            var csharpCode = generator.Generate(document, settings);

            // save output
            File.WriteAllText(csharpOutputPath, csharpCode);

            // save open API document
            var openApiDocumentOutputPath = $"../../../../../openapi.json";
            File.WriteAllText(openApiDocumentOutputPath, openApiJsonString);
        }
    }
}