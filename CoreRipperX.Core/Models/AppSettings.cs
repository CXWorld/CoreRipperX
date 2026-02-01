using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreRipperX.Core.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private int _runtimePerCycleSeconds = 10;

    [ObservableProperty]
    private double _criticalDeviationPercent = 1.0;

    [ObservableProperty]
    private string _selectedAlgorithm = "AVX2 Mixed 1T";

    [ObservableProperty]
    private int _pollingRateMs = 1000;

    [ObservableProperty]
    private int _criticalTemperatureCelsius = 90;

    public string[] AvailableAlgorithms { get; } =
    [
        "AVX2 Mixed 1T",
        "AVX2 Mixed nT",
        "AVX2 Compute 1T",
        "AVX2 Compute nT",
        "AVX2 FP64 1T",
        "AVX2 FP64 nT",
        "AVX512 Mixed 1T",
        "AVX512 Mixed nT",
        "AVX512 Compute 1T",
        "AVX512 Compute nT",
        "AVX512 FP64 1T",
        "AVX512 FP64 nT"
    ];

    /// <summary>
    /// Algorithms that can be used for single-core stress tests (context menu).
    /// </summary>
    public string[] SingleThreadAlgorithms { get; } =
    [
        "AVX2 Mixed 1T",
        "AVX2 Compute 1T",
        "AVX2 FP64 1T",
        "AVX512 Mixed 1T",
        "AVX512 Compute 1T",
        "AVX512 FP64 1T"
    ];

    public int[] AvailablePollingRates { get; } = [250, 500, 1000, 2000];
}
