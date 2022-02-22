﻿using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexus.OpenApiClientGenerator
{
    public class CSharpGenerator
    {
        public string Generate(OpenApiDocument document, GeneratorSettings settings)
        {
            var sourceTextBuilder = new StringBuilder();

            // add clients
            var groupedClients = document.Paths
                .GroupBy(path => path.Value.Operations.First().Value.OperationId.Split(new[] { '_' }, 2).First());

            var subClients = groupedClients.Select(group => group.Key);

            // SubClientFields
            sourceTextBuilder.Clear();
            
            foreach (var subClient in subClients)
            {
                sourceTextBuilder.AppendLine($"    private {subClient}Client _{subClient};");
            }

            var subClientFields = sourceTextBuilder.ToString();

            // SubClientFieldAssignments
            sourceTextBuilder.Clear();

            foreach (var subClient in subClients)
            {
                sourceTextBuilder.AppendLine($"        _{subClient} = new {subClient}Client(this);");
            }

            var subClientFieldAssignments = sourceTextBuilder.ToString();

            // SubClientProperties
            sourceTextBuilder.Clear();

            foreach (var subClient in subClients)
            {
                sourceTextBuilder.AppendLine($"    public I{subClient}Client {subClient} => _{subClient};");
            }

            var subClientProperties = sourceTextBuilder.ToString();

            // SubClientSource
            sourceTextBuilder.Clear();

            foreach (var clientGroup in groupedClients)
            {
                AppendSubClientSourceText(
                    clientGroup.Key,
                    clientGroup.ToDictionary(entry => entry.Key, entry => entry.Value),
                    sourceTextBuilder,
                    settings);

                sourceTextBuilder.AppendLine();
            }

            var subClientSource = sourceTextBuilder.ToString();

            // Models
            sourceTextBuilder.Clear();

            foreach (var schema in document.Components.Schemas)
            {
                AppendModelSourceText(
                    schema.Key,
                    schema.Value,
                    sourceTextBuilder);

                sourceTextBuilder.AppendLine();
            }

            var models = sourceTextBuilder.ToString();

            // Build final source text
            var template = File
                .ReadAllText("Template.cs")
                .Replace("{", "{{")
                .Replace("}", "}}");

            template = Regex
                .Replace(template, "{{([0-9]+)}}", match => $"{{{match.Groups[1].Value}}}");

            return string.Format(
                template,
                settings.Namespace,
                settings.ClientName,
                settings.NexusConfigurationHeaderKey,
                settings.AuthorizationHeaderKey,
                subClientFields,
                subClientFieldAssignments,
                subClientProperties,
                subClientSource,
                settings.ExceptionType,
                models);
        }

        private void AppendSubClientSourceText(
            string className,
            IDictionary<string, OpenApiPathItem> methodMap,
            StringBuilder sourceTextBuilder,
            GeneratorSettings settings)
        {
            className += "Client";

            // interface
            sourceTextBuilder.AppendLine(
$@"public interface I{className}
{{");

            foreach (var entry in methodMap)
            {
                if (entry.Value.Parameters.Any())
                    throw new Exception("Parameters on the path item level are not supported.");

                foreach (var operation in entry.Value.Operations)
                {
                    AppendInterfaceMethodSourceText(entry.Key, operation.Key, operation.Value, sourceTextBuilder);
                }
            }

            sourceTextBuilder.AppendLine("}");
            sourceTextBuilder.AppendLine();

            // implementation
            sourceTextBuilder.AppendLine(
$@"public class {className} : I{className}
{{
    private {settings.ClientName} _client;
    
    internal {className}({settings.ClientName} client)
    {{
        _client = client;
    }}
");

            foreach (var entry in methodMap)
            {
                if (entry.Value.Parameters.Any())
                    throw new Exception("Parameters on the path item level are not supported.");

                foreach (var operation in entry.Value.Operations)
                {
                    AppendImplementationMethodSourceText(
                        entry.Key,
                        operation.Key, 
                        operation.Value,
                        sourceTextBuilder);

                    sourceTextBuilder.AppendLine();
                }
            }

            sourceTextBuilder.AppendLine("}");
        }

        private void AppendInterfaceMethodSourceText(
            string path,
            OperationType operationType,
            OpenApiOperation operation,
            StringBuilder sourceTextBuilder)
        {
            var signature = GetMethodSignature(
                operationType,
                operation,
                out var returnType,
                out var _,
                out var _);

            var preparedReturnType = string.IsNullOrWhiteSpace(returnType)
                ? returnType
                : $"<{returnType}>";

            sourceTextBuilder.AppendLine($"    Task{preparedReturnType} {signature};");
        }

        private void AppendImplementationMethodSourceText(
            string path,
            OperationType operationType,
            OpenApiOperation operation,
            StringBuilder sourceTextBuilder)
        {
            var signature = GetMethodSignature(
                operationType,
                operation,
                out var returnType, 
                out var parameters,
                out var bodyParameter);

            sourceTextBuilder
                .AppendLine("    /// <summary>")
                .AppendLine($"    /// {operation.Summary}")
                .AppendLine("    /// </summary>");

            var isVoidReturnType = string.IsNullOrWhiteSpace(returnType);
            var actualReturnType = isVoidReturnType ? "" : $"<{returnType}>";

            sourceTextBuilder
                .AppendLine($"    public Task{actualReturnType} {signature}")
                .AppendLine($"    {{");

            sourceTextBuilder
                .AppendLine("        var urlBuilder = new StringBuilder();")
                .AppendLine($"        urlBuilder.Append(\"{path}\");");

            // path parameters
            var pathParameters = parameters
                .Where(parameter => parameter.Item2.In == ParameterLocation.Path)
                .ToList();

            foreach (var parameter in pathParameters)
            {
                var parameterName = parameter.Item1.Split(" ")[1];
                sourceTextBuilder.AppendLine($"        urlBuilder.Replace(\"{{{parameterName}}}\", Uri.EscapeDataString(Convert.ToString({parameterName}, CultureInfo.InvariantCulture)));");
            }

            // query parameters
            var queryParameters = parameters
                .Where(parameter => parameter.Item2.In == ParameterLocation.Query)
                .ToList();

            if (queryParameters.Any())
            {
                sourceTextBuilder.AppendLine();
                sourceTextBuilder.AppendLine("        var queryValues = new Dictionary<string, string>()");
                sourceTextBuilder.AppendLine("        {");

                foreach (var parameter in queryParameters)
                {
                    var parameterName = parameter.Item1.Split(" ")[1];
                    var parameterValue = $"Uri.EscapeDataString(Convert.ToString({parameterName}, CultureInfo.InvariantCulture))";

                    sourceTextBuilder.AppendLine($"            [\"{parameterName}\"] = {parameterValue},");
                }

                sourceTextBuilder.AppendLine("        };");
                sourceTextBuilder.AppendLine();
                sourceTextBuilder.AppendLine("        var query = \"?\" + string.Join('&', queryValues.Select(entry => $\"{entry.Key}={entry.Value}\"));");
                sourceTextBuilder.AppendLine("        urlBuilder.Append(query);");
            }

            // url
            sourceTextBuilder.AppendLine();
            sourceTextBuilder.Append("        var url = urlBuilder.ToString();");
            sourceTextBuilder.AppendLine();

            if (isVoidReturnType)
                returnType = "object";

            var content = operation.Responses.First().Value.Content.FirstOrDefault();

            var acceptHeaderValue = content.Equals(default)
                ? "default"
                : $"\"{content.Key}\"";

            var contentValue = bodyParameter is null
                ? "default"
                : bodyParameter.Split(" ")[1];

            sourceTextBuilder.AppendLine($"        return _client.InvokeAsync<{returnType}>(\"{operationType.ToString().ToUpper()}\", url, {acceptHeaderValue}, {contentValue}, cancellationToken);");
            sourceTextBuilder.AppendLine($"    }}");
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

        private string GetType(string mediaTypeKey, OpenApiMediaType mediaType)
        {
            return mediaTypeKey switch
            {
                "application/octet-stream" => "StreamResponse",
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

        private string GetMethodSignature(
            OperationType operationType,
            OpenApiOperation operation, 
            out string returnType,
            out IEnumerable<(string, OpenApiParameter)> parameters,
            out string? bodyParameter)
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

            returnType = response.Content.Count switch
            {
                0 => string.Empty,
                1 => $"{GetType(response.Content.Keys.First(), response.Content.Values.First())}",
                _ => throw new Exception("Only zero or one response contents are supported.")
            };


            parameters = Enumerable.Empty<(string, OpenApiParameter)>();
            bodyParameter = default;

            if (!operation.Parameters.Any() && operation.RequestBody is null)
            {
                return $"{asyncMethodName}(CancellationToken cancellationToken = default)";
            }

            else
            {
                if (operation.Parameters.Any(parameter 
                    => parameter.In != ParameterLocation.Path && parameter.In != ParameterLocation.Query))
                    throw new Exception("Only path or query parameters are supported.");

                parameters = operation.Parameters
                    .Select(parameter => ($"{GetType(parameter.Schema)} {parameter.Name}", parameter));
                
                if (operation.RequestBody is not null)
                {
                    if (operation.RequestBody.Content.Count() != 1)
                        throw new Exception("Only a single request body content is supported.");

                    var content = operation.RequestBody.Content.First();

                    if (content.Key != "application/json")
                        throw new Exception("Only body content media type application/json is supported.");

                    if (!operation.RequestBody.Extensions.TryGetValue("x-name", out var value))
                        throw new Exception("x-name extension is missing.");

                    var name = value as OpenApiString;
                    
                    if (name is null)
                        throw new Exception("The actual x-name value type is not supported.");

                    var type = GetType(content.Key, content.Value);
                    bodyParameter = $"{type} {name.Value}";
                }

                var parametersString = bodyParameter == default
                    ? string.Join(", ", parameters.Select(parameter => parameter.Item1))
                    : string.Join(", ", parameters.Select(parameter => parameter.Item1).Concat(new[] { bodyParameter }));

                return $"{asyncMethodName}({parametersString}, CancellationToken cancellationToken = default)";
            }
        }
    }
}