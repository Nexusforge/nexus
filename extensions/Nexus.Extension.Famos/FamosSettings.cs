using Nexus.Extensibility;
using System.Runtime.Serialization;

namespace Nexus.Extension.Famos
{
    [DataContract]
    [ExtensionIdentification("FamosImc2", "FAMOS (imc2)", "Store data in FAMOS v2 .dat files (imc2).")]
    public class FamosSettings
    {
        #region "Constructors"

        public FamosSettings()
        {
            //
        }

        #endregion
    }
}
