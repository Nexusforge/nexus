using Nexus.Extensibility;
using System.Collections.Generic;

namespace Nexus.DataModel
{
    public class NexusDatabaseConfig
    {
        #region Constructors

        public NexusDatabaseConfig()
        {
            this.AggregationDataReaderRootPath = "";
            this.DataSourceRegistrations = new List<DataSourceRegistration>();
        }

        #endregion

        #region Properties

        public string AggregationDataReaderRootPath { get; set; }

        public List<DataSourceRegistration> DataSourceRegistrations { get; set; }

        #endregion
    }
}