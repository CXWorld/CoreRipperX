using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreRipperX.Core.Models;

public partial class CoreData : ObservableObject
{
    [ObservableProperty]
    private int _coreIndex;

    [ObservableProperty]
    private string _coreLabel = string.Empty;

    [ObservableProperty]
    private float _clockSpeed;

    [ObservableProperty]
    private float _effectiveClockSpeed;

    [ObservableProperty]
    private float _effectiveClockSpeed2T;

    [ObservableProperty]
    private float _load1T;

    [ObservableProperty]
    private float _load2T;

    [ObservableProperty]
    private bool _isDeviationCritical;

    /// <summary>
    /// Which thread is currently being tested on this core.
    /// -1 = not testing, 0 = thread 1, 1 = thread 2
    /// </summary>
    [ObservableProperty]
    private int _testingThreadIndex = -1;

    public bool IsCurrentlyTesting => TestingThreadIndex >= 0;

    /// <summary>
    /// Gets the effective clock speed for the thread currently being tested,
    /// or thread 1 if not testing.
    /// </summary>
    public float ActiveEffectiveClockSpeed => TestingThreadIndex == 1
        ? EffectiveClockSpeed2T
        : EffectiveClockSpeed;

    /// <summary>
    /// Relative deviation between clock speed and effective clock speed (absolute value).
    /// </summary>
    public float DeviationPercent => ClockSpeed > 0 && ActiveEffectiveClockSpeed > 0
        ? Math.Abs((ClockSpeed - ActiveEffectiveClockSpeed) / ClockSpeed * 100f)
        : 0f;

    public void UpdateDeviationStatus(double threshold)
    {
        IsDeviationCritical = Math.Abs(DeviationPercent) > threshold;
        OnPropertyChanged(nameof(DeviationPercent));
        OnPropertyChanged(nameof(IsCurrentlyTesting));
        OnPropertyChanged(nameof(ActiveEffectiveClockSpeed));
    }
}
