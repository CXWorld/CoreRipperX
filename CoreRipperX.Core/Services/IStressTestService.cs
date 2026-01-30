using CoreRipperX.Core.Models;

namespace CoreRipperX.Core.Services;

public record StressTestProgress(
    int CurrentCoreIndex,
    int TotalCores,
    bool IsRunning,
    string Status,
    int ErrorCount = 0,
    string? LastError = null
);

public interface IStressTestService
{
    bool IsRunning { get; }
    IObservable<StressTestProgress> ProgressStream { get; }

    Task RunStressTestAsync(AppSettings settings, CancellationToken cancellation = default);
    Task RunStressTestOnCoreAsync(int physicalCoreIndex, int threadsPerCore, AppSettings settings, CancellationToken cancellation = default);
    Task RunStressTestOnCoreAsync(CoreData coreData, AppSettings settings, CancellationToken cancellation = default);
    void Cancel();
}
