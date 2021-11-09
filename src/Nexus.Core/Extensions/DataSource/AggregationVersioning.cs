using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Extensions
{
    internal class AggregationVersioning
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
            var jsonString = File.ReadAllText(filePath);
            return JsonSerializerHelper.Deserialize<AggregationVersioning>(jsonString);
        }

        #endregion
    }
}
