using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreRipperX.Core.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private int _runtimePerCycleSeconds = 10;

    [ObservableProperty]
    private double _criticalDeviationPercent = 1.0;

    [ObservableProperty]
    private string _selectedAlgorithm = "AVX2";

    [ObservableProperty]
    private int _pollingRateMs = 1000;

    [ObservableProperty]
    private int _criticalTemperatureCelsius = 90;

    public string[] AvailableAlgorithms { get; } = ["AVX2"];

    public int[] AvailablePollingRates { get; } = [250, 500, 1000, 2000];
}
