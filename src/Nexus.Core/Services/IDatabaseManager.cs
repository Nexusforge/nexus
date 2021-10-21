using System.Collections.Generic;
using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        bool TryReadProject(out Stream stream);

        bool TryReadNews(out Stream stream);

        IEnumerable<string> EnumerateAttachements(string catalogId);

        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);

        bool TryReadCatalogMetadata(string catalogId, out Stream? stream);

        Stream WriteCatalogMetadata(string catalogId);

        Stream WriteExportFile(string fileName);
    }
}