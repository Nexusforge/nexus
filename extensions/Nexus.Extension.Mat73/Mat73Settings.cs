using Nexus.Extensibility;
using System.Runtime.Serialization;

namespace Nexus.Extension.Mat73
{
    [DataContract]
    [ExtensionContext(typeof(Mat73Writer))]
    [ExtensionIdentification("MAT73", "Matlab files (v7.3)", "Store data in Matlab's hierachical data format (v7.3).")]
    public class Mat73Settings : DataWriterExtensionSettingsBase
    {
        #region "Constructors"

        public Mat73Settings()
        {
            //
        }

        #endregion

        #region "Methods"

        public override void Validate()
        {
            base.Validate();
        }

        #endregion
    }
}
