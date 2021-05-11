using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Nexus.Core;
using Nexus.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NexusDatabase = Nexus.Database.NexusDatabase;

namespace Nexus.Roslyn
{
    public class RoslynProject
    {
        #region Fields

        private static object _mefLock = new object();

        #endregion

        #region Constructors

        static RoslynProject()
        {
            using var streamReader1 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.DefaultFilterCodeTemplate.cs"));
            RoslynProject.DefaultFilterCode = streamReader1.ReadToEnd();

            using var streamReader2 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.DefaultSharedCodeTemplate.cs"));
            RoslynProject.DefaultSharedCode = streamReader2.ReadToEnd();
        }

        public RoslynProject(CodeDefinition filter, List<string> additionalCodeFiles, NexusDatabase database = null)
        {
            var isRealBuild = database is null;

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
            if (database is not null)
            {
                // shared code
                using var streamReader = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.FilterTypesShared.cs"));

                var sharedCode = streamReader
                    .ReadToEnd()
                    .Replace("Func<string, string, string, DateTime, DateTime, double[]> getData", "DataProvider dataProvider");

                this.Workspace.AddDocument(project.Id, "FilterTypesShared.cs", SourceText.From(sharedCode));

                // database code
                var databaseCode = this.GenerateDatabaseCode(database, filter.SampleRate, filter.RequestedProjectIds);
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

        private string GenerateDatabaseCode(NexusDatabase database, string sampleRate, List<string> requestedProjectIds)
        {
            // generate code
            var classStringBuilder = new StringBuilder();

            classStringBuilder.AppendLine($"using System;");
            classStringBuilder.AppendLine($"namespace {nameof(Nexus)}.Filters");
            classStringBuilder.AppendLine($"{{");

            classStringBuilder.AppendLine($"public class DataProvider");
            classStringBuilder.AppendLine($"{{");

            // add Read() method
            classStringBuilder.AppendLine($"public double[] Read(string projectId, string channelName, string datasetId)");
            classStringBuilder.AppendLine($"{{");
            classStringBuilder.AppendLine($"return new double[0];");
            classStringBuilder.AppendLine($"}}");

            classStringBuilder.AppendLine($"public double[] Read(string projectId, string channelName, string datasetId, DateTime begin, DateTime end)");
            classStringBuilder.AppendLine($"{{");
            classStringBuilder.AppendLine($"return new double[0];");
            classStringBuilder.AppendLine($"}}");

            if (sampleRate is not null)
            {
                var filteredProjectContainer = database.ProjectContainers
                    .Where(projectContainer => requestedProjectIds.Contains(projectContainer.Id));

                foreach (var projectContainer in filteredProjectContainer)
                {
                    var addProject = false;
                    var projectStringBuilder = new StringBuilder();

                    // project class definition
                    projectStringBuilder.AppendLine($"public class {projectContainer.PhysicalName}_TYPE");
                    projectStringBuilder.AppendLine($"{{");

                    foreach (var channel in projectContainer.Project.Channels)
                    {
                        var addChannel = false;
                        var channelStringBuilder = new StringBuilder();

                        // channel class definition
                        channelStringBuilder.AppendLine($"public class {channel.Name}_TYPE");
                        channelStringBuilder.AppendLine($"{{");

                        foreach (var dataset in channel.Datasets.Where(dataset => dataset.Id.Contains(sampleRate)))
                        {
                            // dataset property
                            channelStringBuilder.AppendLine($"public double[] {NexusUtilities.EnforceNamingConvention(dataset.Id, prefix: "DATASET")} {{ get; set; }}");

                            addChannel = true;
                            addProject = true;
                        }

                        channelStringBuilder.AppendLine($"}}");

                        // channel property
                        channelStringBuilder.AppendLine($"public {channel.Name}_TYPE {channel.Name} {{ get; }}");

                        if (addChannel)
                            projectStringBuilder.AppendLine(channelStringBuilder.ToString());
                    }

                    projectStringBuilder.AppendLine($"}}");

                    // project property
                    projectStringBuilder.AppendLine($"public {projectContainer.PhysicalName}_TYPE {projectContainer.PhysicalName} {{ get; }}");

                    if (addProject)
                        classStringBuilder.AppendLine(projectStringBuilder.ToString());
                }
            }

            classStringBuilder.AppendLine($"}}");

            classStringBuilder.AppendLine($"}}");
            return classStringBuilder.ToString();
        }

        #endregion
    }
}