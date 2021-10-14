using Nexus.Utilities;
using System;
using System.Collections.Generic;

namespace Nexus.Extensions
{
    public class AggregationVersioning
    {
        #region Constructors

        public AggregationVersioning()
        {
            this.ScannedUntilMap = new Dictionary<string, DateTime>();
        }

        #endregion

        #region Properties

        public Dictionary<string, DateTime> ScannedUntilMap { get; set; }

        #endregion

        #region Methods

        public static AggregationVersioning Load(string filePath)
        {
            return JsonSerializerHelper.DeserializeFile<AggregationVersioning>(filePath);
        }

        #endregion
    }
}
