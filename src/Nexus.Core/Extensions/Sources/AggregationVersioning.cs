using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nexus.Sources
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
            return JsonSerializer.Deserialize<AggregationVersioning>(jsonString) ?? throw new Exception("aggregation versioning is null");
        }

        #endregion
    }
}
