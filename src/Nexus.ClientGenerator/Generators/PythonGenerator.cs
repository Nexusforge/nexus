using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexus.ClientGenerator
{
    public class PythonGenerator
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
                sourceTextBuilder.AppendLine($"    _{Shared.FirstCharToLower(subClient)}: {subClient}Client");
            }

            var subClientFields = sourceTextBuilder.ToString();

            // SubClientFieldAssignments
            sourceTextBuilder.Clear();

            foreach (var subClient in subClients)
            {
                sourceTextBuilder.AppendLine($"        self._{Shared.FirstCharToLower(subClient)} = {subClient}Client(self)");
            }

            var subClientFieldAssignments = sourceTextBuilder.ToString();

            // SubClientProperties
            sourceTextBuilder.Clear();

            foreach (var subClient in subClients)
            {
                sourceTextBuilder.AppendLine(
$@"    @property
    def {Shared.ToSnakeCase(subClient)}(self) -> {subClient}Client:
        """"""Gets the {subClient}Client.""""""
        return self._{Shared.FirstCharToLower(subClient)}
");
            }

            var subClientProperties = sourceTextBuilder.ToString();

            // SubClientInterfaceProperties
            var subClientInterfaceProperties = string.Empty;

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
            var basePath = Assembly.GetExecutingAssembly().Location;

            var template = File
                .ReadAllText(Path.Combine(basePath, "..", "Templates", "PythonTemplate.py"))
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
                models,
                subClientInterfaceProperties);
        }

        private void AppendSubClientSourceText(
            string className,
            IDictionary<string, OpenApiPathItem> methodMap,
            StringBuilder sourceTextBuilder,
            GeneratorSettings settings)
        {
            var augmentedClassName = className + "Client";

            // interface
            /* nothing to do here */

            // implementation
            sourceTextBuilder.AppendLine(
$@"class {augmentedClassName}:
    """"""Provides methods to interact with {Shared.SplitCamelCase(className).ToLower()}.""""""

    _client: {settings.ClientName}
    
    def __init__(self, client: {settings.ClientName}):
        self._client = client
");

            foreach (var entry in methodMap)
            {
                if (entry.Value.Parameters.Any())
                    throw new Exception("Parameters on the path item level are not supported.");

                foreach (var operation in entry.Value.Operations)
                {
                    AppendImplementationMethodSourceText(
                        path: entry.Key,
                        operation.Key,
                        operation.Value,
                        sourceTextBuilder);

                    sourceTextBuilder.AppendLine();
                }
            }
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

            var isVoidReturnType = string.IsNullOrWhiteSpace(returnType);
            var actualReturnType = isVoidReturnType ? "None" : $"{returnType}";

            sourceTextBuilder
                .AppendLine(
@$"    def {signature} -> Awaitable[{actualReturnType}]:
        """"""{operation.Summary}""""""
        ");

            sourceTextBuilder
                .AppendLine($"        url: str = \"{path}\"");

            // path parameters
            var pathParameters = parameters
                .Where(parameter => parameter.Item2.In == ParameterLocation.Path)
                .ToList();

            foreach (var parameter in pathParameters)
            {
                var originalParameterName = parameter.Item2.Name;
                var parameterName = parameter.Item1.Split(":")[0];
                sourceTextBuilder.AppendLine($"        url = url.replace(\"{{{originalParameterName}}}\", quote(str({parameterName}), safe=\"\"))");
            }

            // query parameters
            var queryParameters = parameters
                .Where(parameter => parameter.Item2.In == ParameterLocation.Query)
                .ToList();

            if (queryParameters.Any())
            {
                sourceTextBuilder.AppendLine();
                sourceTextBuilder.AppendLine("        queryValues: dict[str, str] = {");

                foreach (var parameter in queryParameters)
                {
                    var originalParameterName = parameter.Item2.Name;
                    var parameterName = parameter.Item1.Split(":")[0];
                    var parameterValue = $"quote(to_string({parameterName}), safe=\"\")";

                    sourceTextBuilder.AppendLine($"            \"{originalParameterName}\": {parameterValue},");
                }

                sourceTextBuilder.AppendLine("        }");
                sourceTextBuilder.AppendLine();
                sourceTextBuilder.AppendLine("        query: str = \"?\" + \"&\".join(f\"{key}={value}\" for (key, value) in queryValues.items())");
                sourceTextBuilder.AppendLine("        url += query");
            }

            if (isVoidReturnType)
                returnType = "type(None)";

            var content = operation.Responses.First().Value.Content.FirstOrDefault();

            var acceptHeaderValue = content.Equals(default)
                ? "None"
                : $"\"{content.Key}\"";

            var contentValue = bodyParameter is null
                ? "None"
                : bodyParameter.Split(":")[0];

            sourceTextBuilder.AppendLine();
            sourceTextBuilder.AppendLine($"        return self._client.invoke_async({returnType}, \"{operationType.ToString().ToUpper()}\", url, {acceptHeaderValue}, {contentValue})");
        }

        private void AppendModelSourceText(
            string modelName,
            OpenApiSchema schema,
            StringBuilder sourceTextBuilder)
        {
            // Maybe schema.Extensions[0].x-enumNames would be a better selection.

            if (schema.Enum.Any())
            {
                if (schema.Type != "string")
                    throw new Exception("Only enum of type string is supported.");

                var enumValues = string
                    .Join($",{Environment.NewLine}{Environment.NewLine}", schema.Enum
                    .OfType<OpenApiString>()
                    .Select(current =>
$@"    {Shared.ToSnakeCase(current.Value).ToUpper()} = ""{Shared.ToSnakeCase(current.Value).ToUpper()}""
    """"""{current.Value}"""""""));

                sourceTextBuilder.AppendLine(
@$"class {modelName}(Enum):
    """"""{schema.Description}""""""

{enumValues}");

                sourceTextBuilder.AppendLine();
            }

            else
            {
                sourceTextBuilder
                    .AppendLine(
$@"@dataclass
class {modelName}:");

                sourceTextBuilder.AppendLine(
@$"    """"""{schema.Description}
    Args:");

                if (schema.Properties is not null)
                {
                    foreach (var property in schema.Properties)
                    {
                        sourceTextBuilder.AppendLine($"        {Shared.ToSnakeCase(property.Key)}: {property.Value.Description}");
                    }
                }

                sourceTextBuilder.AppendLine($"    \"\"\"");

                if (schema.Properties is not null)
                {
                    foreach (var property in schema.Properties)
                    {
                        var type = GetType(property.Value);
                        var parameterName = Shared.ToSnakeCase(property.Key);
                        sourceTextBuilder.AppendLine($"    {parameterName}: {type}");
                    }
                }
            }
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
                    ("number", "double", _) => "float",
                    ("integer", "int32", _) => "int",
                    ("string", "uri", _) => "str",
                    ("string", "guid", _) => "UUID",
                    ("string", "duration", _) => "timedelta",
                    ("string", "date-time", _) => "datetime",
                    ("string", _, _) => "str",
                    ("array", _, _) => $"list[{GetType(schema.Items)}]",
                    ("object", _, true) => $"dict[str, {GetType(schema.AdditionalProperties)}]",
                    (_, _, _) => throw new Exception($"The schema type {schema.Type} (or one of its formats) is not supported.")
                };
            }

            else
            {
                type = schema.Reference.Id;
            }

            return schema.Nullable
                ? $"Optional[{type}]"
                : type;
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
                return $"{Shared.ToSnakeCase(asyncMethodName)}(self)";
            }

            else
            {
                if (operation.Parameters.Any(parameter 
                    => parameter.In != ParameterLocation.Path && parameter.In != ParameterLocation.Query))
                    throw new Exception("Only path or query parameters are supported.");

                parameters = operation.Parameters
                    .Select(parameter => ($"{Shared.ToSnakeCase(parameter.Name)}: {GetType(parameter.Schema)}", parameter));
                
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
                    bodyParameter = $"{Shared.ToSnakeCase(name.Value)}: {type}";
                }

                var parametersString = bodyParameter == default
                    ? string.Join(", ", parameters.Select(parameter => parameter.Item1))
                    : string.Join(", ", parameters.Select(parameter => parameter.Item1).Concat(new[] { bodyParameter }));

                return $"{Shared.ToSnakeCase(asyncMethodName)}(self, {parametersString})";
            }
        }
    }
}