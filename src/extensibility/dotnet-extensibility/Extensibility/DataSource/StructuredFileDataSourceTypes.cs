using Nexus.DataModel;

namespace Nexus.Extensibility
{
    /// <summary>
    /// A file source provider provides information about the data file within a database.
    /// </summary>
    /// <param name="Single">A function that takes a catalog item and returns the corresponding file source information.</param>
    /// <param name="All">A function that takes a catalog identifier and that returns all file sources that belong to that catalog.</param>
    public record FileSourceProvider(
        Func<CatalogItem, FileSource> Single, 
        Dictionary<string, FileSource[]> All);

    /// <summary>
    /// A structure to hold information about a file-based database.
    /// </summary>
    /// <param name="PathSegments">A format string that describes the folder structure of the data. An example of a file that is located under the path "group-A/2020-01" would translate into the array ["'group-A'", "yyyy-MM"].</param>
    /// <param name="FileTemplate">A format string that describes the file naming scheme. The template of a file named 20200101_13_my-id_1234.dat would look like "yyyyMMdd_HH'_my-id_????.dat'".</param>
    /// <param name="FileDateTimePreselector">An optional regular expression to select only relevant parts of a file name (e.g. to select the date/time and a unqiue identifier in case there is more than one kind of file in the same folder). In case of a file named 20200101_13_my-id_1234.dat the preselector could be like "(.{11})_my-id".</param>
    /// <param name="FileDateTimeSelector">An optional date/time selector which is mandatory when the preselector is provided. In case of a file named like "20200101_13_my-id_1234.dat", and a preselector of "(.{11})_my-id", the selector should be like "yyyyMMdd_HH".</param>
    /// <param name="FilePeriod">The period per file.</param>
    /// <param name="UtcOffset">The utc offset of the file.</param>
    public record FileSource(
        string[] PathSegments,
        string FileTemplate,
        string? FileDateTimePreselector,
        string? FileDateTimeSelector,
        TimeSpan FilePeriod,
        TimeSpan UtcOffset
    );

    /// <summary>
    /// A structure to hold read information.
    /// </summary>
    /// <param name="FilePath">The path of the file to read.</param>
    /// <param name="CatalogItem">The catalog item to read.</param>
    /// <param name="Data">The data buffer.</param>
    /// <param name="Status">The status buffer.</param>
    /// <param name="FileBegin">The begin date/time of the file.</param>
    /// <param name="FileOffset">The element offset within the file.</param>
    /// <param name="FileBlock">The element count to read from the file.</param>
    /// <param name="FileLength">The expected total number of elements within the file.</param>
    public record ReadInfo(
        string FilePath,
        CatalogItem CatalogItem,
        Memory<byte> Data,
        Memory<byte> Status,
        DateTime FileBegin,
        long FileOffset,
        long FileBlock,
        long FileLength
    );
}
