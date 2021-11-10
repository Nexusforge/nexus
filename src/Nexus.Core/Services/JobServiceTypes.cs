using Nexus.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    public abstract record Job
    {
        /// <example>06f8eb30-5924-4a71-bdff-322f92343f5b</example>
        public Guid Id { get; init; } = Guid.NewGuid();
        /// <example>test@nexus.localhost</example>
        public string Owner { get; init; } = string.Empty;
    }

    public record ExportJob(ExportParameters Parameters) : Job;

    public record AggregationJob(AggregationSetup Setup) : Job;

    public record JobStatus(
        DateTime Start,
        TaskStatus Status,
        double Progress,
        string ExceptionMessage,
        string Result);

    internal record JobControl<T>(
        DateTime Start,
        T Job,
        CancellationTokenSource CancellationTokenSource) where T : Job
    {
        public event EventHandler<double>? ProgressUpdated;
        public event EventHandler? Completed;

        public double Progress { get; private set; }

        public Task<string> Task { get; set; } = null!;

        public void OnProgressUpdated(double e)
        {
            this.Progress = e;
            this.ProgressUpdated?.Invoke(this, e);
        }

        public void OnCompleted()
        {
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
