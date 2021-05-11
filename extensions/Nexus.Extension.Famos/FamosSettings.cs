using Nexus.Extensibility;
using System.Runtime.Serialization;

namespace Nexus.Extension.Famos
{
    [DataContract]
    [ExtensionContext(typeof(FamosWriter))]
    [ExtensionIdentification("FamosImc2", "FAMOS (imc2)", "Store data in FAMOS v2 .dat files (imc2).")]
    public class FamosSettings : DataWriterExtensionSettingsBase
    {
        #region "Constructors"

        public FamosSettings()
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
