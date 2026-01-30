using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using CoreRipperX.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace CoreRipperX.Core.Services;

public class StressTestService : IStressTestService
{
    private const int IterationsPerBatch = 100_000_000;

    private readonly Subject<StressTestProgress> _progressSubject = new();
    private CancellationTokenSource? _cts;
    private int _totalErrorCount;

    public bool IsRunning { get; private set; }
    public IObservable<StressTestProgress> ProgressStream => _progressSubject.AsObservable();

    public async Task RunStressTestAsync(AppSettings settings, CancellationToken cancellation = default)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        IsRunning = true;
        _totalErrorCount = 0;

        int numCores = Environment.ProcessorCount;
        var token = _cts.Token;

        try
        {
            _progressSubject.OnNext(new StressTestProgress(0, numCores, true, "Starting stress test..."));

            for (int i = 0; i < numCores; i++)
            {
                if (token.IsCancellationRequested) break;

                int core = i;
                _progressSubject.OnNext(new StressTestProgress(core, numCores, true, $"Testing Thread #{core}", _totalErrorCount));

                using var coreCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var coreToken = coreCts.Token;

                var task = Task.Run(() => RunStressOnCore(core, settings.SelectedAlgorithm, coreToken), coreToken);

                try
                {
                    await Task.Delay(settings.RuntimePerCycleSeconds * 1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                coreCts.Cancel();

                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }

            var finalStatus = _totalErrorCount > 0
                ? $"Stress test completed with {_totalErrorCount} error(s)!"
                : "Stress test completed - no errors";
            _progressSubject.OnNext(new StressTestProgress(numCores, numCores, false, finalStatus, _totalErrorCount));
        }
        catch (OperationCanceledException)
        {
            _progressSubject.OnNext(new StressTestProgress(0, numCores, false, "Stress test cancelled", _totalErrorCount));
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void RunStressOnCore(int coreIndex, string algorithm, CancellationToken token)
    {
        // Use LibreHardwareMonitor's ThreadAffinity for proper processor group handling
        var previousAffinity = SetThreadAffinity(coreIndex);

        try
        {
            switch (algorithm)
            {
                case "AVX2":
                    RunAvx2Stress(coreIndex, token);
                    break;
                default:
                    RunAvx2Stress(coreIndex, token);
                    break;
            }
        }
        finally
        {
            // Restore previous affinity
            if (previousAffinity != GroupAffinity.Undefined)
            {
                ThreadAffinity.Set(previousAffinity);
            }
        }
    }

    private static GroupAffinity SetThreadAffinity(int coreIndex)
    {
        // Use group 0 for cores 0-63, group 1 for 64-127, etc.
        ushort group = (ushort)(coreIndex / 64);
        int indexInGroup = coreIndex % 64;

        var affinity = GroupAffinity.Single(group, indexInGroup);
        return ThreadAffinity.Set(affinity);
    }

    private void RunAvx2Stress(int coreIndex, CancellationToken token)
    {
        // Expected result: after adding One IterationsPerBatch times, each element = IterationsPerBatch
        var expected = Vector256.Create(IterationsPerBatch);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var vec = Vector256<int>.Zero;
                for (int i = 0; i < IterationsPerBatch; i++)
                {
                    vec = Avx2.Add(vec, Vector256<int>.One);
                }

                // Validate result - if CPU is unstable, computation errors will occur
                if (vec != expected)
                {
                    Interlocked.Increment(ref _totalErrorCount);
                    var errorMsg = $"Thread {coreIndex}: AVX2 computation error detected! Expected {IterationsPerBatch}, got {vec.GetElement(0)}";
                    _progressSubject.OnNext(new StressTestProgress(
                        coreIndex,
                        Environment.ProcessorCount,
                        true,
                        errorMsg,
                        _totalErrorCount,
                        errorMsg));
                }

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation - expected when test duration elapses
        }
        catch (Exception ex)
        {
            // Hardware fault, illegal instruction, or other critical error
            Interlocked.Increment(ref _totalErrorCount);
            var errorMsg = $"Thread {coreIndex}: Critical error - {ex.GetType().Name}: {ex.Message}";
            _progressSubject.OnNext(new StressTestProgress(
                coreIndex,
                Environment.ProcessorCount,
                true,
                errorMsg,
                _totalErrorCount,
                errorMsg));
        }
    }

    public async Task RunStressTestOnCoreAsync(int physicalCoreIndex, int threadsPerCore, AppSettings settings, CancellationToken cancellation = default)
    {
        // Legacy overload - assumes uniform thread distribution (incorrect for hybrid CPUs)
        // Create a temporary CoreData with uniform thread calculation
        var coreData = new CoreData
        {
            CoreIndex = physicalCoreIndex,
            ThreadCount = threadsPerCore,
            FirstLogicalProcessor = physicalCoreIndex * threadsPerCore
        };
        await RunStressTestOnCoreAsync(coreData, settings, cancellation);
    }

    public async Task RunStressTestOnCoreAsync(CoreData coreData, AppSettings settings, CancellationToken cancellation = default)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        IsRunning = true;
        _totalErrorCount = 0;

        var token = _cts.Token;
        int physicalCoreIndex = coreData.CoreIndex;
        int threadCount = coreData.ThreadCount;
        int startThread = coreData.FirstLogicalProcessor;
        int endThread = startThread + threadCount;

        try
        {
            _progressSubject.OnNext(new StressTestProgress(startThread, threadCount, true, $"Testing Core #{physicalCoreIndex}..."));

            for (int i = startThread; i < endThread; i++)
            {
                if (token.IsCancellationRequested) break;

                int thread = i;
                _progressSubject.OnNext(new StressTestProgress(thread, threadCount, true, $"Testing Core #{physicalCoreIndex} Thread #{i - startThread + 1}", _totalErrorCount));

                using var coreCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var coreToken = coreCts.Token;

                var task = Task.Run(() => RunStressOnCore(thread, settings.SelectedAlgorithm, coreToken), coreToken);

                try
                {
                    await Task.Delay(settings.RuntimePerCycleSeconds * 1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                coreCts.Cancel();

                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }

            var finalStatus = _totalErrorCount > 0
                ? $"Core #{physicalCoreIndex} test completed with {_totalErrorCount} error(s)!"
                : $"Core #{physicalCoreIndex} test completed - no errors";
            _progressSubject.OnNext(new StressTestProgress(endThread, threadCount, false, finalStatus, _totalErrorCount));
        }
        catch (OperationCanceledException)
        {
            _progressSubject.OnNext(new StressTestProgress(0, threadCount, false, "Stress test cancelled", _totalErrorCount));
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }
}
