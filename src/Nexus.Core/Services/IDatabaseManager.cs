using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        //void SaveConfig(string folderPath, NexusDatabaseConfig config);

        //void SaveCatalogMeta(CatalogProperties catalogMeta);

        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);
        bool TryReadCatalogMetadata(string catalogId, out Stream? stream);
        Stream WriteCatalogMetadata(string catalogId);

        Stream WriteExportFile(string fileName);
    }
}