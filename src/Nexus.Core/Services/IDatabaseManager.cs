using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        /* /config/catalogs/catalog_id.json */
        bool TryReadCatalogMetadata(string catalogId, [NotNullWhen(true)] out string? catalogMetadata);
        Stream WriteCatalogMetadata(string catalogId);

        /* /users/user_id.json */
        IEnumerable<string> EnumerateUserConfigs();

        /* /config/project.json */
        bool TryReadProject([NotNullWhen(true)] out string? project);

        /* /catalogs/catalog_id/... */
        IEnumerable<string> EnumerateAttachements(string catalogId);
        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, [NotNullWhen(true)] out Stream? attachment);

        /* /export */
        Stream WriteExportFile(string fileName);
    }
}