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
    /// Number of threads on this core (1 for E-cores, 2 for P-cores with SMT).
    /// </summary>
    [ObservableProperty]
    private int _threadCount = 1;

    /// <summary>
    /// The logical processor index of the first thread on this core.
    /// Used for setting thread affinity during stress tests.
    /// </summary>
    [ObservableProperty]
    private int _firstLogicalProcessor;

    /// <summary>
    /// Returns true if this core has multiple threads (P-core with SMT).
    /// </summary>
    public bool HasSecondThread => ThreadCount > 1;

    /// <summary>
    /// Returns the effective clock speed for thread 2, or null if this core has only 1 thread.
    /// Used for display purposes to show empty cells for E-cores.
    /// </summary>
    public float? EffectiveClockSpeed2TDisplay => HasSecondThread ? EffectiveClockSpeed2T : null;

    /// <summary>
    /// Returns the load for thread 2, or null if this core has only 1 thread.
    /// Used for display purposes to show empty cells for E-cores.
    /// </summary>
    public float? Load2TDisplay => HasSecondThread ? Load2T : null;

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
        // Only update IsDeviationCritical if it actually changed
        bool newCritical = Math.Abs(DeviationPercent) > threshold;
        if (IsDeviationCritical != newCritical)
            IsDeviationCritical = newCritical;

        // Only notify for computed properties that depend on values that may have changed
        // DeviationPercent and ActiveEffectiveClockSpeed depend on ClockSpeed/EffectiveClockSpeed
        OnPropertyChanged(nameof(DeviationPercent));
        OnPropertyChanged(nameof(ActiveEffectiveClockSpeed));
    }

    partial void OnThreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSecondThread));
        OnPropertyChanged(nameof(EffectiveClockSpeed2TDisplay));
        OnPropertyChanged(nameof(Load2TDisplay));
    }

    partial void OnEffectiveClockSpeed2TChanged(float value)
    {
        OnPropertyChanged(nameof(EffectiveClockSpeed2TDisplay));
    }

    partial void OnLoad2TChanged(float value)
    {
        OnPropertyChanged(nameof(Load2TDisplay));
    }

    partial void OnTestingThreadIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCurrentlyTesting));
        OnPropertyChanged(nameof(ActiveEffectiveClockSpeed));
    }
}
