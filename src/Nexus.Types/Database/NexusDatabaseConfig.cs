using System.Collections.Generic;

namespace Nexus.Database
{
    public class NexusDatabaseConfig
    {
        #region Constructors

        public NexusDatabaseConfig()
        {
            this.AggregationDataReaderRootPath = "";
            this.DataReaderRegistrations = new List<DataReaderRegistration>();
        }

        #endregion

        #region Properties

        public string AggregationDataReaderRootPath { get; set; }

        public List<DataReaderRegistration> DataReaderRegistrations { get; set; }

        #endregion
    }
}