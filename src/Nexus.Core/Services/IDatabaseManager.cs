using System.IO;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        //void SaveConfig(string folderPath, NexusDatabaseConfig config);

        //void SaveCatalogMeta(CatalogProperties catalogMeta);

        bool TryReadFirstAttachment(string catalogId, string searchPattern, EnumerationOptions enumerationOptions, out Stream attachment);

        Stream WriteExportFile(string fileName);
    }
}