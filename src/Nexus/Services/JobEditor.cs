using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Nexus.Services
{
    public class JobEditor
    {
        #region Events

        public event EventHandler Changed;

        #endregion

        #region Fields

        private string _jsonString;
        private CatalogState _state;

        #endregion

        #region Constructors

        public JobEditor(AppState appState)
        {
            _state = appState.CatalogState;
            this.Update();
        }

        #endregion

        #region Properties

        public AggregationSetup AggregationSetup { get; private set; } = new AggregationSetup();

        public string Analysis { get; private set; }

        public string JsonString
        {
            get
            {
                return _jsonString;
            }
            set
            {
                _jsonString = value;

                try
                {
                    this.AggregationSetup = JsonSerializer.Deserialize<AggregationSetup>(_jsonString);

                    this.Update(skipJson: true);
                    this.OnChanged();
                }
                catch
                {
                    //
                }
            }
        }

        #endregion

        #region Methods

        public void Update(bool skipJson = false)
        {
            // analysis
            var instructions = AggregationService.ComputeInstructions(this.AggregationSetup, _state, NullLogger.Instance);
            var sb = new StringBuilder();

            foreach (var instruction in instructions)
            {
                sb.AppendLine($"Catalog '{instruction.Container.Id}'");

                foreach (var (backendSource, aggregationResources) in instruction.DataReaderToAggregationsMap)
                {
                    if (aggregationResources.Any())
                    {
                        sb.AppendLine();
                        sb.AppendLine($"\tData Reader '{backendSource.Type}' ({backendSource.ResourceLocator})");

                        foreach (var aggregationResource in aggregationResources)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"\t\t{aggregationResource.Resource.Id} / {aggregationResource.Resource.Properties.GetValueOrDefault(DataModelExtensions.Unit, string.Empty)}");

                            foreach (var aggregation in aggregationResource.Aggregations)
                            {
                                foreach (var period in aggregation.Periods)
                                {
                                    sb.Append($"\t\t\tPeriod: {period} s, ");

                                    foreach (var method in aggregation.Methods)
                                    {
                                        sb.Append($" {method.Key}");
                                    }

                                    sb.AppendLine();
                                }
                            }
                        }
                    }
                }

                sb.AppendLine();
            }

            this.Analysis = sb.ToString();

            // json
            if (!skipJson)
            {
                var options = new JsonSerializerOptions() { WriteIndented = true };
                _jsonString = JsonSerializer.Serialize(this.AggregationSetup, options);
            }
        }

        private void OnChanged()
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
