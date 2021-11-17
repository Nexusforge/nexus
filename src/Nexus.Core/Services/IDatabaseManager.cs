using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        bool TryReadProject([NotNullWhen(true)] out string? project);

        bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata);

        IEnumerable<string> EnumerateAttachements(string catalogId);

        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment);

        Stream WriteCatalogMetadata(string catalogId);

        Stream WriteExportFile(string fileName);
    }
}