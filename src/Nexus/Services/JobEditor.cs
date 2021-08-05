using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using static Nexus.Services.DatabaseManager;

namespace Nexus.Services
{
    internal class JobEditor
    {
        #region Events

        public event EventHandler Changed;

        #endregion

        #region Fields

        private string _jsonString;
        private DatabaseManagerState _state;

        #endregion

        #region Constructors

        public JobEditor(IDatabaseManager databaseManager)
        {
            _state = databaseManager.State;
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
                            sb.AppendLine($"\t\t{aggregationResource.Resource.Name} / {aggregationResource.Resource.Group} / {aggregationResource.Resource.Unit}");

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
