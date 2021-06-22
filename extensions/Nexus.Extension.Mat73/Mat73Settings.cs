using Nexus.Extensibility;
using System.Runtime.Serialization;

namespace Nexus.Extension.Mat73
{
    [DataContract]
    [ExtensionIdentification("MAT73", "Matlab files (v7.3)", "Store data in Matlab's hierachical data format (v7.3).")]
    public class Mat73Settings
    {
        #region "Constructors"

        public Mat73Settings()
        {
            //
        }

        #endregion
    }
}
