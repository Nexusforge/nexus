using ChartJs.Blazor.BarChart;
using ChartJs.Blazor.BarChart.Axes;
using ChartJs.Blazor.Common;
using ChartJs.Blazor.Common.Axes;
using ChartJs.Blazor.Common.Axes.Ticks;
using ChartJs.Blazor.Common.Enums;
using ChartJs.Blazor.Common.Time;
using MatBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Core;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Nexus.Shared
{
    public partial class AvailabilityBox
    {
        #region Fields

        private string[] _backgroundColors;
        private string[] _borderColors;

        #endregion

        #region Constructors

        public AvailabilityBox()
        {
            // https://developer.mozilla.org/de/docs/Web/CSS/CSS_Colors/farbauswahl_werkzeug
            _backgroundColors = new string[]
            {
                "rgba(136, 14, 79, 0.2)",
                "rgba(14, 75, 133, 0.2)",
                "rgba(14, 133, 131, 0.2)"
            };

            _borderColors = new string[]
            {
                "rgba(136, 14, 79)",
                "rgba(14, 75, 133)",
                "rgba(14, 133, 131)"
            };

            this.Config = new BarConfig
            {
                Options = new BarOptions
                {
                    Legend = new Legend
                    {
                        Display = true
                    },
                    MaintainAspectRatio = false,
                    Scales = new BarScales
                    {
                        XAxes = new List<CartesianAxis>
                        {
                            new BarTimeAxis
                            {
                                BarPercentage = 0.9,
                                Offset = true,
                                Time = new TimeOptions
                                {
                                    Unit = TimeMeasurement.Day
                                },
                                Ticks = new TimeTicks
                                {
                                    FontColor = "var (--font-color)",
                                    FontSize = 15
                                }
                            }
                        },
                        YAxes = new List<CartesianAxis>
                        {
                            new LinearCartesianAxis
                            {
                                Position = Position.Left,
                                ScaleLabel = new ScaleLabel
                                {
                                    Display = false
                                },
                                Ticks = new LinearCartesianTicks
                                {
                                    Max = 100,
                                    Min = 0,
                                    BeginAtZero = true,
                                    FontColor = "var (--font-color)",
                                    FontSize = 15
                                }
                            }
                        }
                    },
                    Tooltips = new Tooltips
                    {
                        Enabled = false
                    }
                }
            };
        }

        #endregion

        #region Properties

        [Inject]
        private AppState AppState { get; set; }

        [Inject]
        private ToasterService ToasterService { get; set; }

        private BarConfig Config { get; set; }

        #endregion

        #region Methods

        protected override void OnInitialized()
        {
            this.PropertyChanged = (sender, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(UserState.DateTimeBegin):
                    case nameof(UserState.DateTimeEnd):
                    case nameof(UserState.CatalogContainer):

                        _ = this.UpdateChartAsync();

                        break;

                    default:
                        break;
                }
            };

            base.OnInitialized();
        }

        private int iteration = 0;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // The problem here is that the child chart is not yet fully initialized. The chart initializes itself in it's OnAfterRenderMethod,
            // which is called AFTER this method. So the following code is needed to let the child initialize first before we call Chart.Update();.
            // Reference: https://github.com/dotnet/aspnetcore/issues/13781#issuecomment-531257109

            // 1. render: trigger another render
            if (firstRender)
                this.StateHasChanged();

            // 2. render: update chart
            if (iteration == 1)
                await this.UpdateChartAsync();

            iteration++;

            await base.OnAfterRenderAsync(firstRender);
        }

        private async Task UpdateChartAsync()
        {
            var totalDays = (int)(this.UserState.DateTimeEnd.Date - this.UserState.DateTimeBegin.Date).TotalDays;

            if (totalDays < 0)
                return;

            var axis = (BarTimeAxis)((BarConfig)_barChart.Config).Options.Scales.XAxes[0];

            try
            {
                var granularity = totalDays <= 365
                    ? AvailabilityGranularity.Day
                    : AvailabilityGranularity.Month;

                var availability = await this.UserState.GetAvailabilityAsync(granularity, CancellationToken.None);
                var hasCleared = false;

                if (availability.Length != this.Config.Data.Datasets.Count)
                {
                    this.Config.Data.Datasets.Clear();
                    hasCleared = true;
                }

                for (int i = 0; i < availability.Length; i++)
                {
                    BarDataset<TimePoint> representation;

                    if (hasCleared)
                    {
                        var backendSource = availability[i].BackendSource;
                        var isAggregation = backendSource.Equals(this.AppState.CatalogState.AggregationBackendSource);

                        representation = new BarDataset<TimePoint>
                        {
                            Label = isAggregation ? "Aggregations" : $"Raw ({backendSource.ResourceLocator} - {backendSource.Type})",
                            BackgroundColor = _backgroundColors[i % _backgroundColors.Count()],
                            BorderColor = _borderColors[i % _borderColors.Count()],
                            BorderWidth = 2
                        };

                        this.Config.Data.Datasets.Add(representation);
                    }
                    else
                    {
                        representation = (BarDataset<TimePoint>)this.Config.Data.Datasets[i];
                        representation.Clear();
                    }

                    switch (granularity)
                    {
                        case AvailabilityGranularity.Day:

                            axis.Time.Unit = TimeMeasurement.Day;

                            representation.AddRange(availability[i].Data
                                .Select((entry, i) =>
                                {
                                    return new TimePoint(entry.Key, entry.Value * 100);
                                })
                            );

                            break;

                        case AvailabilityGranularity.Month:

                            axis.Time.Unit = TimeMeasurement.Month;

                            representation.AddRange(availability[i].Data
                                .Select((entry, i) =>
                                {
                                    return new TimePoint(entry.Key, entry.Value * 100);
                                })
                            );

                            break;

                        default:
                            break;
                    }
                }

                await _barChart.Update();
            }
            catch (TaskCanceledException)
            {
                // prevent that the whole app crashes in the followig case:
                // - Nexus calculates aggregations and locks current file
                // GUI wants to load data from that locked file and times out
                // TaskCanceledException is thrown: app crashes.
                this.UserState.ClientState = ClientState.Normal;
            }
            catch (Exception ex)
            {
                this.UserState.Logger.LogError(ex, "Load availability data failed.");
                this.ToasterService.ShowError(message: "Unable to load availability data.", icon: MatIconNames.Error_outline);
            }
        }

        #endregion
    }
}
