using System.Reactive.Linq;
using System.Runtime.Intrinsics.X86;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreRipperX.Core.Models;
using CoreRipperX.Core.Services;

namespace CoreRipperX.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IStressTestService _stressTestService;
    private IDisposable? _progressSubscription;
    private ThreadPriority? _uiThreadPriorityBeforeStress;

    public AppSettings Settings { get; }
    public IReadOnlyList<AlgorithmOption> AlgorithmOptions { get; }

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
        AlgorithmOptions = BuildAlgorithmOptions(settings.AvailableAlgorithms);

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
        if (value)
        {
            if (_uiThreadPriorityBeforeStress == null)
            {
                _uiThreadPriorityBeforeStress = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
        }
        else if (_uiThreadPriorityBeforeStress != null)
        {
            Thread.CurrentThread.Priority = _uiThreadPriorityBeforeStress.Value;
            _uiThreadPriorityBeforeStress = null;
        }

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _progressSubscription?.Dispose();
    }

    private static IReadOnlyList<AlgorithmOption> BuildAlgorithmOptions(IEnumerable<string> algorithms)
    {
        var list = new List<AlgorithmOption>();
        foreach (var algo in algorithms)
        {
            list.Add(new AlgorithmOption(algo,
                GetLoadGroupOrder(algo),
                GetAvxGroupOrder(algo),
                GetThreadOrder(algo)));
        }
        return list;
    }

    private static int GetLoadGroupOrder(string algorithm)
    {
        if (algorithm.Contains("Compute", StringComparison.OrdinalIgnoreCase))
            return 3; // Heavy
        if (algorithm.Contains("FP64", StringComparison.OrdinalIgnoreCase))
            return 2; // Medium
        return 1; // Light
    }

    private static int GetAvxGroupOrder(string algorithm)
        => algorithm.StartsWith("AVX512", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

    private static int GetThreadOrder(string algorithm)
        => algorithm.Contains("1T", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    public sealed class AlgorithmOption
    {
        public AlgorithmOption(string algorithmKey, int loadGroupOrder, int avxGroupOrder, int threadOrder)
        {
            AlgorithmKey = algorithmKey;
            DisplayName = BuildDisplayName(algorithmKey);
            LoadGroupOrder = loadGroupOrder;
            AvxGroupOrder = avxGroupOrder;
            ThreadOrder = threadOrder;
            IsAvailable = CheckAvailability(algorithmKey);
        }

        public string AlgorithmKey { get; }
        public string DisplayName { get; }
        public int LoadGroupOrder { get; }
        public int AvxGroupOrder { get; }
        public int ThreadOrder { get; }
        public bool IsAvailable { get; }

        private static string BuildDisplayName(string algorithmKey)
        {
            // Remove AVX prefix since it's shown in group header
            var name = algorithmKey
                .Replace("AVX512 ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("AVX2 ", "", StringComparison.OrdinalIgnoreCase);

            // Rename the standard test to "Mixed"
            if (name == "1T" || name == "nT")
                name = "Mixed " + name;

            // Mark unavailable algorithms
            if (!CheckAvailability(algorithmKey))
                name += " (N/A)";

            return name;
        }

        private static bool CheckAvailability(string algorithmKey)
        {
            if (algorithmKey.StartsWith("AVX512", StringComparison.OrdinalIgnoreCase))
                return Avx512F.IsSupported;
            return true;
        }
    }
}
