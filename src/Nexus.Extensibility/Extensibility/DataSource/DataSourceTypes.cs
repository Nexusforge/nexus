using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record ReadInfo<T>(
        string FilePath,
        Dataset Dataset,
        Memory<T> Data,
        Memory<byte> Status,
        long Offset,
        long ExpectedTotalSize
    ) where T : unmanaged;

    public record DataSourceRegistration
    {
        public string RootPath { get; set; }
        public string DataSourceId { get; set; }
    }

    public record SourceDescription(
        string[] PathSegments,
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
