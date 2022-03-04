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
        public const string NexusConfigurationHeaderKey = "Nexus-Configuration";
        public const string AuthorizationHeaderKey = "Authorization";

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

            // generate clients
            var basePath = Assembly.GetExecutingAssembly().Location;

            // generate C# client
            var csharpSettings = new GeneratorSettings(
                Namespace: "Nexus.Api",
                ClientName: "NexusClient",
                ExceptionType: "NexusException");

            var csharpOutputPath = $"{solutionRoot}src/clients/cs-client/NexusClient.g.cs";
            var csharpGenerator = new CSharpGenerator();
            var csharpCode = csharpGenerator.Generate(document, csharpSettings);

            File.WriteAllText(csharpOutputPath, csharpCode);

            // generate Python client
            var pythonSettings = new GeneratorSettings(
                Namespace: default,
                ClientName: "NexusAsyncClient",
                ExceptionType: "NexusException");

            var pythonOutputPath = $"{solutionRoot}src/clients/python-client/nexusapi/_nexusapi.py";
            var pythonGenerator = new PythonGenerator();
            var pythonCode = pythonGenerator.Generate(document, pythonSettings);

            File.WriteAllText(pythonOutputPath, pythonCode);

            // save open API document
            var openApiDocumentOutputPath = $"{solutionRoot}{openApiFileName}";
            File.WriteAllText(openApiDocumentOutputPath, openApiJsonString);
        }
    }
}