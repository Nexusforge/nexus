using System.Collections.Generic;
using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        bool TryReadProject(out string? project);

        bool TryReadNews(out string? news);

        bool TryReadCatalogMetadata(string catalogId, out string? catalogMetadata);

        IEnumerable<string> EnumerateAttachements(string catalogId);

        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream? attachment);

        Stream WriteCatalogMetadata(string catalogId);

        Stream WriteExportFile(string fileName);
    }
}