using CoreRipperX.Core.Models;

namespace CoreRipperX.Core.Services;

public interface IHardwareMonitorService : IDisposable
{
    string CpuName { get; }
    int PhysicalCoreCount { get; }
    int LogicalCoreCount { get; }
    int ThreadsPerCore { get; }
    bool IsHybridCpu { get; }
    int SensorCount { get; }
    float PackagePowerWatts { get; }
    float PackageTemperatureCelsius { get; }
    string? LastError { get; }
    string? DiagnosticInfo { get; }

    IObservable<IReadOnlyList<CoreData>> CoreDataStream { get; }

    void StartMonitoring(TimeSpan interval);
    void StopMonitoring();
    IReadOnlyList<CoreData> GetCurrentCoreData();
}
