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

    [ObservableProperty]
    private bool _isHybridCpu;

    /// <summary>
    /// Returns true if any core has multiple threads (for showing 2T columns).
    /// This is true for hybrid CPUs (some P-cores have 2 threads) or uniform SMT CPUs.
    /// </summary>
    public bool HasMultipleThreadsPerCore => IsHybridCpu || ThreadsPerCore > 1;

    [ObservableProperty]
    private int _sensorCount;

    [ObservableProperty]
    private string _packagePowerDisplay = "-";

    [ObservableProperty]
    private string _packageTemperatureDisplay = "-";

    [ObservableProperty]
    private bool _isTemperatureCritical;

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

    public string[] SingleThreadAlgorithms => _settings.SingleThreadAlgorithms;

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
        IsHybridCpu = _hardwareService.IsHybridCpu;
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

        // Update package power and temperature displays
        var power = _hardwareService.PackagePowerWatts;
        var temp = _hardwareService.PackageTemperatureCelsius;
        PackagePowerDisplay = power > 0 ? $"{power:N0}W" : "-";
        PackageTemperatureDisplay = temp > 0 ? $"{temp:N0}\u00B0C" : "-";
        IsTemperatureCritical = temp > 0 && temp >= _settings.CriticalTemperatureCelsius;

        // Update CPU info if not set
        if (CpuName == "Loading..." || CpuName == "Unknown CPU")
        {
            CpuName = _hardwareService.CpuName;
            PhysicalCoreCount = _hardwareService.PhysicalCoreCount;
            LogicalCoreCount = _hardwareService.LogicalCoreCount;
            ThreadsPerCore = _hardwareService.ThreadsPerCore;
            IsHybridCpu = _hardwareService.IsHybridCpu;
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
            target.ThreadCount = source.ThreadCount;
            target.FirstLogicalProcessor = source.FirstLogicalProcessor;
            target.UpdateDeviationStatus(_settings.CriticalDeviationPercent);
        }
    }

    private void OnStressTestProgress(StressTestProgress progress)
    {
        // Update stress test status
        IsStressTestRunning = progress.IsRunning;
        StressTestStatus = progress.Status;
        StressTestErrorCount = progress.ErrorCount;

        // Don't mark rows for nT tests (all cores stressed simultaneously)
        var algorithm = _settings.SelectedAlgorithm;
        bool isMultiThreaded = algorithm.EndsWith("nT") || algorithm == "Memory Chase";

        if (isMultiThreaded)
        {
            // Clear all row highlighting for nT tests
            for (int i = 0; i < Cores.Count; i++)
            {
                Cores[i].TestingThreadIndex = -1;
            }
        }
        else
        {
            // Map logical thread index to physical core and thread within core
            // This handles hybrid CPUs where P-cores have 2 threads and E-cores have 1 thread
            int logicalProcessorIndex = progress.CurrentCoreIndex;
            int physicalCoreIndex = -1;
            int threadWithinCore = 0;

            // Find which physical core contains this logical processor
            for (int i = 0; i < Cores.Count; i++)
            {
                var core = Cores[i];
                int coreStart = core.FirstLogicalProcessor;
                int coreEnd = coreStart + core.ThreadCount;

                if (logicalProcessorIndex >= coreStart && logicalProcessorIndex < coreEnd)
                {
                    physicalCoreIndex = i;
                    threadWithinCore = logicalProcessorIndex - coreStart;
                    break;
                }
            }

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
        }

        // Notify command CanExecute changes
        StressTestCoreCommand.NotifyCanExecuteChanged();
        CancelStressTestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStressTestCore))]
    private async Task StressTestCoreAsync(CoreData? core)
    {
        if (core == null) return;

        // Use the CoreData directly - it contains the correct FirstLogicalProcessor and ThreadCount
        // which properly handles hybrid CPUs (P-cores with 2 threads, E-cores with 1 thread)
        await _stressTestService.RunStressTestOnCoreAsync(core, _settings);
    }

    private bool CanStressTestCore(CoreData? core) => core != null && !_stressTestService.IsRunning;

    [RelayCommand(CanExecute = nameof(CanStressTestCoreWithAlgorithm))]
    private async Task StressTestCoreWithAlgorithmAsync(StressTestCoreRequest? request)
    {
        if (request?.Core == null || string.IsNullOrEmpty(request.Algorithm)) return;

        // Temporarily override the algorithm for this test
        var originalAlgorithm = _settings.SelectedAlgorithm;
        _settings.SelectedAlgorithm = request.Algorithm;

        try
        {
            await _stressTestService.RunStressTestOnCoreAsync(request.Core, _settings);
        }
        finally
        {
            _settings.SelectedAlgorithm = originalAlgorithm;
        }
    }

    private bool CanStressTestCoreWithAlgorithm(StressTestCoreRequest? request) =>
        request?.Core != null && !string.IsNullOrEmpty(request.Algorithm) && !_stressTestService.IsRunning;

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
