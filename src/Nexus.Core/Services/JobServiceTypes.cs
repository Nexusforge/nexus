﻿using Nexus.Services;
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

    public record ExportJob : Job
    {
        public ExportParameters Parameters { get; init; }
    }

    public record AggregationJob : Job
    {
        public AggregationSetup Setup { get; init; }
    }

    public record JobStatus
    {
        public DateTime Start { get; init; }
        public TaskStatus Status { get; init; }
        public double Progress { get; init; }
        public string ExceptionMessage { get; init; }
        public string Result { get; init; }
    }

    internal record JobControl<T> where T : Job
    {
        public event EventHandler<double> ProgressUpdated;
        public event EventHandler Completed;

        public DateTime Start { get; init; }
        public double Progress { get; private set; }
        public T Job { get; init; }
        public Task<string> Task { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; init; }

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