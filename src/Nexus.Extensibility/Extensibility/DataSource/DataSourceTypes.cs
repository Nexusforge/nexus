using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record ReadResult<T>(ReadOnlyMemory<T> Dataset, ReadOnlyMemory<byte> Status)
    {
        public int Length => this.Dataset.Length;
    }

    public record DataSourceRegistration
    {
        public string RootPath { get; set; }
        public string DataSourceId { get; set; }
    }

    public record SourceDescription(
        List<string> PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
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
