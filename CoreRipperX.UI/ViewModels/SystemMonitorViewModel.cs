using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Threading;

namespace CoreRipperX.UI.ViewModels;

public partial class SystemMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareMonitorService _hardwareService;
    private readonly IStressTestService _stressTestService;
    private readonly AppSettings _settings;
    private IDisposable? _subscription;
    private IDisposable? _stressTestSubscription;
    private long _lastCoreUiUpdateTicks;
    private long _lastProgressUiUpdateTicks;

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

    public bool IsAvx512Supported => Avx512F.IsSupported;

    public string Avx512HeaderText => Avx512F.IsSupported ? "AVX512" : "AVX512 (N/A)";

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

        // Use dispatcher with Send priority for highest priority async UI updates
        // This ensures UI updates are processed immediately even under heavy CPU load
        // Note: We avoid Rx Sample/Throttle operators as they use ThreadPool timers which get
        // starved during heavy AVX operations. Instead, throttling is done in the callbacks.
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Subscribe directly - throttling handled in OnCoreDataUpdated via _lastCoreUiUpdateTicks
        _subscription = _hardwareService.CoreDataStream
            .Subscribe(data => dispatcher.InvokeAsync(() => OnCoreDataUpdated(data), DispatcherPriority.Send));

        // Subscribe directly - throttling handled in OnStressTestProgress via _lastProgressUiUpdateTicks
        _stressTestSubscription = _stressTestService.ProgressStream
            .Subscribe(progress => dispatcher.InvokeAsync(() => OnStressTestProgress(progress), DispatcherPriority.Send));

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
        var minIntervalMs = IsStressTestRunning ? 250 : 100;
        var nowTicks = Stopwatch.GetTimestamp();
        var elapsedMs = (nowTicks - _lastCoreUiUpdateTicks) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < minIntervalMs)
            return;
        _lastCoreUiUpdateTicks = nowTicks;

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

            // Only update properties that have actually changed to reduce PropertyChanged events
            if (target.ClockSpeed != source.ClockSpeed)
                target.ClockSpeed = source.ClockSpeed;
            if (target.EffectiveClockSpeed != source.EffectiveClockSpeed)
                target.EffectiveClockSpeed = source.EffectiveClockSpeed;
            if (target.EffectiveClockSpeed2T != source.EffectiveClockSpeed2T)
                target.EffectiveClockSpeed2T = source.EffectiveClockSpeed2T;
            if (target.Load1T != source.Load1T)
                target.Load1T = source.Load1T;
            if (target.Load2T != source.Load2T)
                target.Load2T = source.Load2T;
            if (target.ThreadCount != source.ThreadCount)
                target.ThreadCount = source.ThreadCount;
            if (target.FirstLogicalProcessor != source.FirstLogicalProcessor)
                target.FirstLogicalProcessor = source.FirstLogicalProcessor;

            target.UpdateDeviationStatus(_settings.CriticalDeviationPercent);
        }
    }

    private void OnStressTestProgress(StressTestProgress progress)
    {
        // Throttle progress updates to 150ms, but always allow state changes through
        var nowTicks = Stopwatch.GetTimestamp();
        var elapsedMs = (nowTicks - _lastProgressUiUpdateTicks) * 1000 / Stopwatch.Frequency;
        bool stateChanged = IsStressTestRunning != progress.IsRunning;
        if (elapsedMs < 150 && !stateChanged)
            return;
        _lastProgressUiUpdateTicks = nowTicks;

        // Update stress test status
        IsStressTestRunning = progress.IsRunning;
        StressTestStatus = progress.Status;
        StressTestErrorCount = progress.ErrorCount;

        // Don't mark rows for nT tests (all cores stressed simultaneously)
        var algorithm = _settings.SelectedAlgorithm;
        bool isMultiThreaded = algorithm.EndsWith("nT");

        if (isMultiThreaded)
        {
            // Clear all row highlighting for nT tests - only update if changed
            for (int i = 0; i < Cores.Count; i++)
            {
                if (Cores[i].TestingThreadIndex != -1)
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

            // Only update TestingThreadIndex when the value actually changes
            for (int i = 0; i < Cores.Count; i++)
            {
                int newValue = (progress.IsRunning && i == physicalCoreIndex) ? threadWithinCore : -1;
                if (Cores[i].TestingThreadIndex != newValue)
                    Cores[i].TestingThreadIndex = newValue;
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
