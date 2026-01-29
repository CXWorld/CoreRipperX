using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;

namespace CoreRipperX.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IStressTestService _stressTestService;
    private IDisposable? _progressSubscription;

    public AppSettings Settings { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private int _currentCore;

    [ObservableProperty]
    private int _totalCores;

    [ObservableProperty]
    private double _progress;

    public SettingsViewModel(AppSettings settings, IStressTestService stressTestService)
    {
        Settings = settings;
        _stressTestService = stressTestService;
        TotalCores = Environment.ProcessorCount;

        _progressSubscription = _stressTestService.ProgressStream
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(OnProgressUpdate);
    }

    private void OnProgressUpdate(StressTestProgress progress)
    {
        IsRunning = progress.IsRunning;
        Status = progress.Status;
        CurrentCore = progress.CurrentCoreIndex;
        TotalCores = progress.TotalCores;
        Progress = TotalCores > 0 ? (double)CurrentCore / TotalCores * 100 : 0;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await _stressTestService.RunStressTestAsync(Settings);
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _stressTestService.Cancel();
    }

    private bool CanStop() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _progressSubscription?.Dispose();
    }
}
