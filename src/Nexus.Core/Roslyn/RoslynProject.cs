using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexus.Roslyn
{
    public class RoslynProject
    {
        #region Fields

        private static object _mefLock = new();

        #endregion

        #region Constructors

        static RoslynProject()
        {
            using var streamReader1 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Core.Resources.DefaultFilterCodeTemplate.cs"));
            RoslynProject.DefaultFilterCode = streamReader1.ReadToEnd();

            using var streamReader2 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Core.Resources.DefaultSharedCodeTemplate.cs"));
            RoslynProject.DefaultSharedCode = streamReader2.ReadToEnd();
        }

        public RoslynProject(CodeDefinition filter, List<string> additionalCodeFiles, CatalogCollection catalogs = null)
        {
            var isRealBuild = catalogs is null;

            MefHostServices host;

            // Lock is required because the RoslynProject may be instantiated concurrently.
            lock (_mefLock) {
                host = MefHostServices.Create(MefHostServices.DefaultAssemblies);

                // workspace
                this.Workspace = new AdhocWorkspace(host);
            }

            // project
            var projectInfo = ProjectInfo
                .Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Nexus", "Nexus", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

            if (isRealBuild)
            {
                projectInfo = projectInfo
                    .WithMetadataReferences(Net50.All.Concat(new List<PortableExecutableReference>() { MetadataReference.CreateFromFile(typeof(FilterProviderBase).Assembly.Location) }));
            }
            else
            {
                projectInfo = projectInfo
                    .WithMetadataReferences(Net50.All);
            }

            var project = this.Workspace.AddProject(projectInfo);

            // actual code
            var document = this.Workspace.AddDocument(project.Id, "Code.cs", SourceText.From(filter.Code));
            this.DocumentId = document.Id;

            // additional code
            foreach (var additionalCode in additionalCodeFiles)
            {
                this.Workspace.AddDocument(project.Id, Guid.NewGuid().ToString(), SourceText.From(additionalCode));
            }

            // other code
            if (catalogs is not null)
            {
                // shared code
                using var streamReader = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Core.Resources.FilterTypesShared.cs"));

                var sharedCode = streamReader
                    .ReadToEnd()
                    .Replace("GetFilterData getData", "DataProvider dataProvider");

                this.Workspace.AddDocument(project.Id, "FilterTypesShared.cs", SourceText.From(sharedCode));

                // database code
                var databaseCode = this.GenerateDatabaseCode(catalogs, filter.SamplePeriod, filter.RequestedCatalogIds);
                this.Workspace.AddDocument(project.Id, "DatabaseCode.cs", SourceText.From(databaseCode));
            }
        }

        #endregion

        #region Properties

        public static string DefaultFilterCode { get; }

        public static string DefaultSharedCode { get; }

        public AdhocWorkspace Workspace { get; init; }

        public DocumentId DocumentId { get; init; }

        #endregion

        #region Methods

        public void UpdateCode(DocumentId documentId, string code)
        {
            if (code == null)
                return;

            Solution updatedSolution;

            do
            {
                updatedSolution = this.Workspace.CurrentSolution.WithDocumentText(documentId, SourceText.From(code));
            } while (!this.Workspace.TryApplyChanges(updatedSolution));
        }

        private string GenerateDatabaseCode(CatalogCollection catalogs, TimeSpan samplePeriod, List<string> requestedCatalogIds)
        {
            // generate code
            var classStringBuilder = new StringBuilder();

            classStringBuilder.AppendLine($"using System;");
            classStringBuilder.AppendLine($"namespace {nameof(Nexus)}.Filters");
            classStringBuilder.AppendLine($"{{");

            classStringBuilder.AppendLine($"public class DataProvider");
            classStringBuilder.AppendLine($"{{");

            // add Read() method
            classStringBuilder.AppendLine($"public Span<double> Read(string catalogId, string resourceName, string representationId)");
            classStringBuilder.AppendLine($"{{");
            classStringBuilder.AppendLine($"return default;");
            classStringBuilder.AppendLine($"}}");

            classStringBuilder.AppendLine($"public Span<double> Read(string catalogId, string resourceName, string representationId, DateTime begin, DateTime end)");
            classStringBuilder.AppendLine($"{{");
            classStringBuilder.AppendLine($"return default;");
            classStringBuilder.AppendLine($"}}");

            var filteredCatalogContainer = catalogs.CatalogContainers
                    .Where(catalogContainer => requestedCatalogIds.Contains(catalogContainer.Id));

            foreach (var catalogContainer in filteredCatalogContainer)
            {
                var addCatalog = false;
                var catalogStringBuilder = new StringBuilder();

                // catalog class definition
                catalogStringBuilder.AppendLine($"public class {catalogContainer.PhysicalName}_TYPE");
                catalogStringBuilder.AppendLine($"{{");

                foreach (var resource in catalogContainer.Catalog.Resources)
                {
                    var addResource = false;
                    var resourceStringBuilder = new StringBuilder();

                    // resource class definition
                    resourceStringBuilder.AppendLine($"public class {resource.Name}_TYPE");
                    resourceStringBuilder.AppendLine($"{{");

                    foreach (var representation in resource.Representations.Where(representation => representation.SamplePeriod == samplePeriod))
                    {
                        // representation property
                        resourceStringBuilder.AppendLine($"public Span<double> {ExtensibilityUtilities.EnforceNamingConvention(representation.Id, prefix: "REPRESENTATION")} {{ get; set; }}");

                        addResource = true;
                        addCatalog = true;
                    }

                    resourceStringBuilder.AppendLine($"}}");

                    // resource property
                    resourceStringBuilder.AppendLine($"public {resource.Name}_TYPE {resource.Name} {{ get; }}");

                    if (addResource)
                        catalogStringBuilder.AppendLine(resourceStringBuilder.ToString());
                }

                catalogStringBuilder.AppendLine($"}}");

                // catalog property
                catalogStringBuilder.AppendLine($"public {catalogContainer.PhysicalName}_TYPE {catalogContainer.PhysicalName} {{ get; }}");

                if (addCatalog)
                    classStringBuilder.AppendLine(catalogStringBuilder.ToString());
            }

            classStringBuilder.AppendLine($"}}");

            classStringBuilder.AppendLine($"}}");
            return classStringBuilder.ToString();
        }

        #endregion
    }
}