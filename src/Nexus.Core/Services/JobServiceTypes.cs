using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    /// <summary>
    /// Description of a job.
    /// </summary>
    /// <param name="Id"><example>06f8eb30-5924-4a71-bdff-322f92343f5b</example></param>
    /// <param name="Owner"><example>test@nexus.localhost</example></param>
    /// <param name="Type"><example>export</example></param>
    /// <param name="Parameters">Job parameters.</param>
    public record Job(Guid Id, string Type, string Owner, object Parameters);

    public record JobStatus(
        DateTime Start,
        TaskStatus Status,
        double Progress,
        string ExceptionMessage,
        object Result);

    internal record JobControl(
        DateTime Start,
        Job Job,
        CancellationTokenSource CancellationTokenSource)
    {
        public event EventHandler<double>? ProgressUpdated;
        public event EventHandler? Completed;

        public double Progress { get; private set; }

        public Task<object> Task { get; set; }

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
