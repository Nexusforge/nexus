using System;

namespace Nexus.Extensibility
{
    public abstract class DataWriterExtensionSettingsBase : ExtensionSettingsBase
    {
        #region "Constructors

        public DataWriterExtensionSettingsBase()
        {
            this.FilePeriod = TimeSpan.FromDays(1);
        }

        #endregion

        #region "Properties"

        public TimeSpan FilePeriod { get; set; }

        public bool SingleFile { get; set; }

        #endregion

        #region "Methods"

        public override void Validate()
        {
            base.Validate();

            if (this.FilePeriod == TimeSpan.Zero)
                throw new Exception(ErrorMessage.DataWriterExtensionSettingsBase_FileGranularityInvalid);
        }

        #endregion
    }
}
