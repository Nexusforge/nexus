using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using System.Diagnostics;
using System.Text;

namespace SourceGenerator
{
    [Generator]
    public class HelloSourceGenerator : ISourceGenerator
    {
        private static DiagnosticDescriptor n0000 = new DiagnosticDescriptor(
            "N0000",
            "Error generating OpenAPI client",
            "Error generating OpenAPI client: {0}",
            "Client generation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(GeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
                Debugger.Launch();
#endif
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                // read open API document
                using var stream = File
                    .OpenRead("C:/codegen/swagger.json");

                var document = new OpenApiStreamReader()
                    .Read(stream, out var diagnostic);

                var sourceTextBuilder = new StringBuilder();

                // add namespace
                sourceTextBuilder.AppendLine("namespace Nexus.Client;");
                sourceTextBuilder.AppendLine();

                // add clients
                var groupedClients = document.Paths
                    .GroupBy(path => path.Value.Operations.First().Value.OperationId.Split(new[] { '_' }, 2).First());

                foreach (var clientGroup in groupedClients)
                {
                    AppendClientSourceText(
                        clientGroup.Key,
                        clientGroup.ToDictionary(entry => entry.Key, entry => entry.Value),
                        sourceTextBuilder);

                    sourceTextBuilder.AppendLine();
                }

                // add models
                foreach (var schema in document.Components.Schemas)
                {
                    AppendModelSourceText(
                        schema.Key,
                        schema.Value,
                        sourceTextBuilder);

                    sourceTextBuilder.AppendLine();
                }

                // add resulting code
                var sourceText = sourceTextBuilder.ToString();
                var sourceName = "NexusOpenApi.cs";

                context.AddSource(sourceName, SourceText.From(sourceText, Encoding.UTF8));

#if DEBUG
                File.WriteAllText(Path.Combine("C:/codegen", sourceName), sourceText);
#endif
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(n0000, Location.None, ex.Message));
            }
        }

        private void AppendClientSourceText(
            string className,
            IDictionary<string, OpenApiPathItem> methodMap,
            StringBuilder sourceTextBuilder)
        {
            className += "Client";

            sourceTextBuilder.AppendLine(
$@"public class {className}
{{");

            foreach (var entry in methodMap)
            {
                if (entry.Value.Parameters.Any())
                    throw new Exception("Parameters on the path item level are not supported.");

                foreach (var operation in entry.Value.Operations)
                {
                    AppendMethodSourceText(entry.Key, operation.Key, operation.Value, sourceTextBuilder);
                    sourceTextBuilder.AppendLine();
                }
            }

            sourceTextBuilder.AppendLine("}");
        }

        private void AppendMethodSourceText(
            string path,
            OperationType operationType,
            OpenApiOperation operation,
            StringBuilder sourceTextBuilder)
        {
            if (!(operationType == OperationType.Get ||
                operationType == OperationType.Put ||
                operationType == OperationType.Post ||
                operationType == OperationType.Delete))
                throw new Exception("Only get, put, post or delete operations are supported.");

            var methodName = operation.OperationId.Split(new[] { '_' }, 2)[1];
            var asyncMethodName = methodName + "Async";

            if (operation.Responses.Count() != 1)
                throw new Exception("Only a single response is supported.");

            var responseEntry = operation.Responses.First();
            var responseType = responseEntry.Key;
            var response = responseEntry.Value;

            if (responseType != "200")
                throw new Exception("Only response type '200' is supported.");

            var returnType = response.Content.Count switch
            {
                0 => string.Empty,
                1 => $"<{GetReturnType(response.Content.Keys.First(), response.Content.Values.First())}>",
                _ => throw new Exception("Only zero or one response contents are supported.")
            };

            var parameters = operation.Parameters is null
                ? string.Empty
                : GetParameters(operation.Parameters);

            sourceTextBuilder.AppendLine(
$@"    /// <summary>
    /// {operation.Summary}
    /// </summary>
    public async Task{returnType} {asyncMethodName}({parameters})
    {{
        //
    }}");
        }

        private void AppendModelSourceText(
            string modelName,
            OpenApiSchema schema,
            StringBuilder sourceTextBuilder)
        {
            if (schema.Enum.Any())
            {
                if (schema.Type != "string")
                    throw new Exception("Only enum of type string is supported.");

                var enumValues = string
                    .Join($",{Environment.NewLine}    ", schema.Enum
                    .OfType<OpenApiString>()
                    .Select(current => current.Value));

                sourceTextBuilder.AppendLine(
@$"public enum {modelName}
{{
    {enumValues}
}}");
            }

            else
            {
                var parameters = schema.Properties is null
                   ? string.Empty
                   : GetProperties(schema.Properties);

                sourceTextBuilder.AppendLine($"public record {modelName} ({parameters});");
            }
        }

        private string GetProperties(IDictionary<string, OpenApiSchema> propertyMap)
        {
            var methodParameters = propertyMap.Select(entry =>
            {
                var returnType = GetType(entry.Value);
                var parameterName = FirstCharToUpper(entry.Key);
                return $"{returnType} {parameterName}";
            });

            return string.Join(", ", methodParameters);
        }

        private string GetParameters(IList<OpenApiParameter> parameters)
        {
            var methodParameters = parameters.Select(parameter =>
            {
                return $"{GetType(parameter.Schema)} {parameter.Name}";
            });

            return string.Join(", ", methodParameters);
        }

        private string GetReturnType(string mediaTypeKey, OpenApiMediaType mediaType)
        {
            return mediaTypeKey switch
            {
                "application/octet-stream" => "Stream",
                "application/json" => GetType(mediaType.Schema),
                _ => throw new Exception($"The media type {mediaTypeKey} is not supported.")
            };
        }

        private string GetType(OpenApiSchema schema)
        {
            string type;

            if (schema.Reference is null)
            {
                type = (schema.Type, schema.Format, schema.AdditionalPropertiesAllowed) switch
                {
                    (null, _, _) => schema.OneOf.Count switch
                    {
                        0 => "object",
                        1 => GetType(schema.OneOf.First()),
                        _ => throw new Exception("Only zero or one entries are supported.")
                    },
                    ("boolean", _,  _) => "bool",
                    ("number", "double", _) => "double",
                    ("string", "uri", _) => "Uri",
                    ("string", "guid", _) => "Guid",
                    ("string", "duration", _) => "TimeSpan",
                    ("string", "date-time", _) => "DateTime",
                    ("string", _, _) => "string",
                    ("array", _, _) => $"ICollection<{GetType(schema.Items)}>",
                    ("object", _, true) => $"IDictionary<string, {GetType(schema.AdditionalProperties)}>",
                    (_, _, _) => throw new Exception($"The schema type {schema.Type} (or one of its formats) is not supported.")
                };
            }

            else
            {
                type = schema.Reference.Id;
            }

            return schema.Nullable
                ? $"{type}?"
                : type;
        }

        // https://stackoverflow.com/questions/4135317/make-first-letter-of-a-string-upper-case-with-maximum-performance?rq=1
        private static string FirstCharToUpper(string input)
        {
            return input switch
            {
                null => throw new ArgumentNullException(nameof(input)),
                "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
                _ => input[0].ToString().ToUpper() + input.Substring(1)
            };
        }
    }
}