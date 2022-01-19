using Nexus.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace Nexus.Services
{
    internal class JobService<T> where T : Job
    {
        #region Fields

        private Timer _timer;

        private ConcurrentDictionary<Guid, JobControl<T>> _jobs = new ConcurrentDictionary<Guid,JobControl<T>>();

        #endregion

        #region Constructors

        public JobService()
        {
            _timer = new Timer()
            {
                AutoReset = true,
                Enabled = true,
                Interval = TimeSpan.FromDays(1).TotalMilliseconds
            };

            _timer.Elapsed += (sender, e) =>
            {
                var now = DateTime.UtcNow;
                var maxRuntime = TimeSpan.FromDays(3);

                foreach (var jobControl in this.GetJobs())
                {
                    if (jobControl.Task.IsCompleted)
                    {
                        var runtime = now - jobControl.Start;

                        if (runtime > maxRuntime)
                            _jobs.TryRemove(jobControl.Job.Id, out _);
                    }
                }
            };
        }

        #endregion

        #region Methods

        public JobControl<T> AddJob(T job, Progress<double> progress, Func<JobControl<T>, CancellationTokenSource, Task<string>> createTask)
        {
            var cancellationTokenSource = new CancellationTokenSource();

            var jobControl = new JobControl<T>(
                Start: DateTime.UtcNow,
                Job: job,
                CancellationTokenSource: cancellationTokenSource);

            var progressHandler = (EventHandler<double>)((sender, e) =>
            {
                jobControl.OnProgressUpdated(e);
            });

            progress.ProgressChanged += progressHandler;
            jobControl.Task = createTask(jobControl, cancellationTokenSource);

            _ = Task.Run(async () =>
            {
                try
                {
                    await jobControl.Task;
                }
                finally
                {
                    jobControl.OnCompleted();
                    jobControl.ProgressUpdated -= progressHandler;
                }
            });

            this.TryAddJob(jobControl);
            return jobControl;
        }

        private bool TryAddJob(JobControl<T> jobControl)
        {
            var result = _jobs.TryAdd(jobControl.Job.Id, jobControl);
            return result;
        }

        public bool TryGetJob(Guid key, [NotNullWhen(true)] out JobControl<T>? jobControl)
        {
            return _jobs.TryGetValue(key, out jobControl);
        }

        public List<JobControl<T>> GetJobs()
        {
            // http://blog.i3arnon.com/2018/01/16/concurrent-dictionary-tolist/
            // https://stackoverflow.com/questions/41038514/calling-tolist-on-concurrentdictionarytkey-tvalue-while-adding-items
            return _jobs
                .ToArray()
                .Select(entry => entry.Value)
                .ToList();
        }

        #endregion
    }
}
