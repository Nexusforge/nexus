using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record SourceDescription(
        List<string> PathSegments,
        string? FileNamePreselector,
        TimeSpan FilePeriod
    );

    public class ProjectDescriptions : Dictionary<string, Dictionary<string, SourceDescription>>
    {
        //
    }

    public class FileSystemDescription
    {
        #region Constructors

        public FileSystemDescription(ProjectDescriptions projects)
        {
            this.Projects = projects;
        }

        #endregion

        #region Properties

        public ProjectDescriptions Projects { get; }

        #endregion
    }
}
