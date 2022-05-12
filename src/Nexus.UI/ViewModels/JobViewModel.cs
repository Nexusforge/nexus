using System.ComponentModel;
using Nexus.Api;
using TaskStatus = Nexus.Api.TaskStatus;

namespace Nexus.UI.ViewModels;

public class JobViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private JobStatus? _status;
    private Job _model;

    public JobViewModel(Job model, ExportParameters parameters, INexusClient client)
    {
        _model = model;
        Parameters = parameters;

        Task.Run(async () =>
        {
            while (Status is null || Status.Status < TaskStatus.RanToCompletion)
            {
                Status = await client.Jobs.GetJobStatusAsync(model.Id, CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            };
        });
    }

    public Guid Id => _model.Id;

    public ExportParameters Parameters { get; }

    public double Progress => _status is null 
        ? 0.0 
        : _status.Progress;

    public JobStatus? Status
    {
        get
        {
            return _status;
        }
        private set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}