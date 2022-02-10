//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using Microsoft.CodeAnalysis.Text;
//using System.Text;

//namespace SourceGenerator
//{
//    [Generator]
//    public class HelloSourceGenerator : ISourceGenerator
//    {
//        private static DiagnosticDescriptor n0000 = new DiagnosticDescriptor(
//            "N0000",
//            "Controllers need a non-static public constructor",
//            "The controller {0} must have a non-static public constructor",
//            "Accessibility",
//            DiagnosticSeverity.Error,
//            isEnabledByDefault: true);

//        public void Initialize(GeneratorInitializationContext context)
//        {
////#if DEBUG
////            if (!Debugger.IsAttached)
////                Debugger.Launch();
////#endif 

//            context.RegisterForSyntaxNotifications(() => new MySyntaxReceiver());
//        }

//        public void Execute(GeneratorExecutionContext context)
//        {
//            var syntaxReceiver = (MySyntaxReceiver)context.SyntaxReceiver!;
//            var sourceTextBuilder = new StringBuilder();

//            sourceTextBuilder.AppendLine("namespace Nexus.Client;");
//            sourceTextBuilder.AppendLine();

//            foreach (var controllerCds in syntaxReceiver.Controllers)
//            {
//                var controllerSm = context.Compilation.GetSemanticModel(controllerCds.SyntaxTree);
//                var controllerSymbol = controllerSm.GetDeclaredSymbol(controllerCds)!;

//                var hasPublicConstructor = controllerSymbol.Constructors
//                    .Any(constructorSymbol => 
//                    constructorSymbol.DeclaredAccessibility == Accessibility.Public &&
//                    !constructorSymbol.IsStatic);

//                if (!hasPublicConstructor)
//                {
//                    context.ReportDiagnostic(Diagnostic.Create(n0000, Location.None, controllerSymbol.Name));
//                    return;
//                }

//                try
//                {
//                    AppendControllerSourceText(controllerSymbol, sourceTextBuilder);
//                    sourceTextBuilder.AppendLine();
//                }
//                catch (Exception ex)
//                {

//                    throw;
//                }
//            }

//            var sourceText = sourceTextBuilder.ToString();
//            var sourceName = "NexusOpenApi.cs";

//            context.AddSource(sourceName, SourceText.From(sourceText, Encoding.UTF8));

//#if DEBUG
//            File.WriteAllText(Path.Combine("C:/codegen", sourceName), sourceText);
//#endif
//        }

//        private void AppendControllerSourceText(
//            INamedTypeSymbol controller,
//            StringBuilder sourceTextBuilder)
//        {
//            var suffix = "Controller";

//            var clientClassName = controller.Name.EndsWith(suffix)
//                ? controller.Name.Substring(0, controller.Name.Length - suffix.Length)
//                : controller.Name;

//            clientClassName += "Client";

//            sourceTextBuilder.Append(
//$@"public class {clientClassName}
//{{
//");

//            var methods = controller.GetMembers().OfType<IMethodSymbol>()
//                .Where(member =>
//                    member.MethodKind == MethodKind.Ordinary &&
//                    member.DeclaredAccessibility == Accessibility.Public &&
//                    !member.IsGenericMethod &&
//                    !member.IsStatic &&
//                    !member.IsAbstract &&
//                    !member.IsExtern);

//            foreach (var method in methods)
//            {
//                AppendMethodSourceText(method, sourceTextBuilder);
//            }

//            sourceTextBuilder.AppendLine("}");
//        }

//        private void AppendMethodSourceText(IMethodSymbol method, StringBuilder sourceTextBuilder)
//        {
//            var clientClassName = method.Name.EndsWith("Async")
//                ? method.Name
//                : $"{method.Name}Async";

//            var returnType = GetReturnType(method.ReturnType);

//            sourceTextBuilder.AppendLine(
//$@"    public async Task<> {clientClassName}
//    {{
//    }}");

//            sourceTextBuilder.AppendLine();
//        }

//        private string GetReturnType(ITypeSymbol returnType)
//        {
//            if (returnType.Name == "Task")
//            {
                
//            }

//            var b = 1;
//            return "nonono";
//        }
//    }

//    static class RoslynExtensions
//    {
//        public static bool HasAttribute(this ClassDeclarationSyntax cds, string attributeName)
//        {
//            return cds.AttributeLists.Any(attributeList
//                => attributeList.Attributes.Any(attribute
//                => attribute.Name.NormalizeWhitespace().ToFullString().Contains(attributeName)));
//        }
//    }

//    class MySyntaxReceiver : ISyntaxReceiver
//    {
//        public List<ClassDeclarationSyntax> Controllers { get; } = new();

//        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
//        {
//            if (syntaxNode is ClassDeclarationSyntax cds)
//            {
//                if (cds.HasAttribute("ApiController"))
//                    Controllers.Add(cds);
//            }
//        }
//    }
//}