using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;

namespace CoreRipperX.UI.ViewModels;

public partial class SystemMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareMonitorService _hardwareService;
    private readonly IStressTestService _stressTestService;
    private readonly AppSettings _settings;
    private IDisposable? _subscription;
    private IDisposable? _stressTestSubscription;

    [ObservableProperty]
    private string _cpuName = "Loading...";

    [ObservableProperty]
    private int _physicalCoreCount;

    [ObservableProperty]
    private int _logicalCoreCount;

    [ObservableProperty]
    private int _threadsPerCore;

    public bool HasMultipleThreadsPerCore => ThreadsPerCore > 1;

    [ObservableProperty]
    private int _sensorCount;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string? _diagnosticInfo;

    [ObservableProperty]
    private string _stressTestStatus = string.Empty;

    [ObservableProperty]
    private int _stressTestErrorCount;

    [ObservableProperty]
    private bool _isStressTestRunning;

    public ObservableCollection<CoreData> Cores { get; } = new();

    public SystemMonitorViewModel(IHardwareMonitorService hardwareService, IStressTestService stressTestService, AppSettings settings)
    {
        _hardwareService = hardwareService;
        _stressTestService = stressTestService;
        _settings = settings;

        // GetCurrentCoreData triggers Initialize()
        var initialData = _hardwareService.GetCurrentCoreData();
        foreach (var core in initialData)
        {
            Cores.Add(core);
        }

        // Read values after initialization
        CpuName = _hardwareService.CpuName;
        PhysicalCoreCount = _hardwareService.PhysicalCoreCount;
        LogicalCoreCount = _hardwareService.LogicalCoreCount;
        ThreadsPerCore = _hardwareService.ThreadsPerCore;
        OnPropertyChanged(nameof(HasMultipleThreadsPerCore));

        _subscription = _hardwareService.CoreDataStream
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(OnCoreDataUpdated);

        _stressTestSubscription = _stressTestService.ProgressStream
            .ObserveOn(SynchronizationContext.Current!)
            .Subscribe(OnStressTestProgress);

        _settings.PropertyChanged += OnSettingsChanged;
        _hardwareService.StartMonitoring(TimeSpan.FromMilliseconds(_settings.PollingRateMs));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.PollingRateMs))
        {
            _hardwareService.StartMonitoring(TimeSpan.FromMilliseconds(_settings.PollingRateMs));
        }
    }

    private void OnCoreDataUpdated(IReadOnlyList<CoreData> coreDataList)
    {
        SensorCount = _hardwareService.SensorCount;
        LastError = _hardwareService.LastError;
        DiagnosticInfo = _hardwareService.DiagnosticInfo;

        // Update CPU info if not set
        if (CpuName == "Loading..." || CpuName == "Unknown CPU")
        {
            CpuName = _hardwareService.CpuName;
            PhysicalCoreCount = _hardwareService.PhysicalCoreCount;
            LogicalCoreCount = _hardwareService.LogicalCoreCount;
            ThreadsPerCore = _hardwareService.ThreadsPerCore;
            OnPropertyChanged(nameof(HasMultipleThreadsPerCore));
        }

        for (int i = 0; i < coreDataList.Count && i < Cores.Count; i++)
        {
            var source = coreDataList[i];
            var target = Cores[i];

            target.ClockSpeed = source.ClockSpeed;
            target.EffectiveClockSpeed = source.EffectiveClockSpeed;
            target.EffectiveClockSpeed2T = source.EffectiveClockSpeed2T;
            target.Load1T = source.Load1T;
            target.Load2T = source.Load2T;
            target.UpdateDeviationStatus(_settings.CriticalDeviationPercent);
        }
    }

    private void OnStressTestProgress(StressTestProgress progress)
    {
        // Update stress test status
        IsStressTestRunning = progress.IsRunning;
        StressTestStatus = progress.Status;
        StressTestErrorCount = progress.ErrorCount;

        // Map logical thread index to physical core and thread within core
        int threadsPerCore = PhysicalCoreCount > 0 ? LogicalCoreCount / PhysicalCoreCount : 1;
        int physicalCoreIndex = threadsPerCore > 0 ? progress.CurrentCoreIndex / threadsPerCore : progress.CurrentCoreIndex;
        int threadWithinCore = threadsPerCore > 0 ? progress.CurrentCoreIndex % threadsPerCore : 0;

        for (int i = 0; i < Cores.Count; i++)
        {
            if (progress.IsRunning && i == physicalCoreIndex)
            {
                Cores[i].TestingThreadIndex = threadWithinCore;
            }
            else
            {
                Cores[i].TestingThreadIndex = -1;
            }
        }

        // Notify command CanExecute changes
        StressTestCoreCommand.NotifyCanExecuteChanged();
        CancelStressTestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStressTestCore))]
    private async Task StressTestCoreAsync(CoreData? core)
    {
        if (core == null) return;

        int threadsPerCore = PhysicalCoreCount > 0 ? LogicalCoreCount / PhysicalCoreCount : 1;
        await _stressTestService.RunStressTestOnCoreAsync(core.CoreIndex, threadsPerCore, _settings);
    }

    private bool CanStressTestCore(CoreData? core) => core != null && !_stressTestService.IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancelStressTest))]
    private void CancelStressTest()
    {
        _stressTestService.Cancel();
    }

    private bool CanCancelStressTest() => _stressTestService.IsRunning;

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _subscription?.Dispose();
        _stressTestSubscription?.Dispose();
        _hardwareService.StopMonitoring();
    }
}
