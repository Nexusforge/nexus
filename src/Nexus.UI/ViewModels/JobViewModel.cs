using System.ComponentModel;
using Nexus.Api;
using TaskStatus = Nexus.Api.TaskStatus;

namespace Nexus.UI.ViewModels;

public class JobViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isCancelled;
    private JobStatus? _status;
    private Job _model;
    private ExportParameters _parameters;

    public JobViewModel(Job model, ExportParameters parameters, INexusClient client)
    {
        _model = model;
        _parameters = parameters;

        Task.Run(async () =>
        {
            do
            {
                Status = await client.Jobs.GetJobStatusAsync(model.Id, CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            } while (!_isCancelled && Status.Status < TaskStatus.RanToCompletion);
        });
    }

    public Guid Id => _model.Id;

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

    public void Cancel()
    {
        _isCancelled = true;
    }
}