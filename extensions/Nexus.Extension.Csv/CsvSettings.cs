using Nexus.Extensibility;
using System.Runtime.Serialization;

namespace Nexus.Extension.Csv
{
    [DataContract]
    [ExtensionIdentification("CSV", "Comma-separated values", "Store data in comma-separated values files.")]
    public class CsvSettings
    {
        #region "Constructors"

        public CsvSettings()
        {
            this.SignificantFigures = 4;
        }

        #endregion

        #region Properties

        public CsvRowIndexFormat RowIndexFormat { get; set; }
        public uint SignificantFigures { get; set; }

        #endregion
    }
}
