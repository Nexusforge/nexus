using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using Nexus.Controllers.V1;
using System.Text;

namespace Nexus.OpenApiClientGenerator
{
    public static class Program
    {
#error TODO:
#error C#: Add authentication, add XML comments, add configuration support via headers:
#error Python: Add client

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services
                .AddMvcCore().AddApplicationPart(typeof(ArtifactsController).Assembly);

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

            using var stream = await response.Content.ReadAsStreamAsync();

            var openApiDocument = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
            stream.Position = 0;

            var document = new OpenApiStreamReader()
                .Read(stream, out var diagnostic);

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
            File.WriteAllText(openApiDocumentOutputPath, openApiDocument);
        }
    }
}