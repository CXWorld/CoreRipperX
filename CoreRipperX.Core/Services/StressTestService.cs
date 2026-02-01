using CoreRipperX.Core.Models;
using CoreRipperX.Core.Tests;
using LibreHardwareMonitor.Hardware;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace CoreRipperX.Core.Services;

public class StressTestService : IStressTestService, IDisposable
{
    private const int MemoryChaseBufferSize = 256 * 1024 * 1024; // 256 MB >> L3 cache
    private const int MemoryChaseHopsPerBurst = 10_000;
    private const int MemoryChaseSleepMs = 5;

    private readonly Subject<StressTestProgress> _progressSubject = new();
    private readonly AggressiveAvx2Stress _avx2Stress = new();
    private readonly AggressiveAvx512Stress _avx512Stress = new();
    private CancellationTokenSource? _cts;
    private int _totalErrorCount;

    public StressTestService()
    {
        StartJitWarmup();
    }

    public bool IsRunning { get; private set; }
    public IObservable<StressTestProgress> ProgressStream => _progressSubject.AsObservable();

    public async Task RunStressTestAsync(AppSettings settings, CancellationToken cancellation = default)
    {
        if (IsRunning) return;

        var algorithm = settings.SelectedAlgorithm;

        // Check AVX512 support before starting
        if (algorithm.StartsWith("AVX512", StringComparison.OrdinalIgnoreCase) && !Avx512F.IsSupported)
        {
            _progressSubject.OnNext(new StressTestProgress(0, Environment.ProcessorCount, false,
                "AVX-512 is not supported on this CPU. Please select an AVX2 algorithm.",
                LastError: "AVX-512 not supported"));
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        IsRunning = true;
        _totalErrorCount = 0;

        int numCores = Environment.ProcessorCount;
        var token = _cts.Token;
        bool isMultiThreaded = algorithm.EndsWith("nT");

        try
        {
            _progressSubject.OnNext(new StressTestProgress(0, numCores, true, "Starting stress test..."));

            if (isMultiThreaded)
            {
                // nT algorithms: stress all cores simultaneously
                await RunMultiThreadedStressAsync(settings, token);
            }
            else
            {
                // 1T algorithms: stress cores one at a time
                await RunSequentialStressAsync(settings, token);
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

    private async Task RunSequentialStressAsync(AppSettings settings, CancellationToken token)
    {
        int numCores = Environment.ProcessorCount;

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
    }

    private async Task RunMultiThreadedStressAsync(AppSettings settings, CancellationToken token)
    {
        int numCores = Environment.ProcessorCount;
        int stressThreads = numCores;
        _progressSubject.OnNext(new StressTestProgress(0, numCores, true, $"Stressing all {stressThreads} threads...", _totalErrorCount));

        using var stressCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stressToken = stressCts.Token;

        // Launch stress on all cores simultaneously
        var tasks = new Task[stressThreads];
        for (int i = 0; i < stressThreads; i++)
        {
            int core = i;
            tasks[i] = Task.Run(() => RunStressOnCore(core, settings.SelectedAlgorithm, stressToken), stressToken);
        }

        try
        {
            // Run for the specified duration
            await Task.Delay(settings.RuntimePerCycleSeconds * 1000, token);
        }
        catch (OperationCanceledException)
        {
            // External cancellation
        }

        stressCts.Cancel();

        // Wait for all tasks to complete
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StartJitWarmup()
    {
        var thread = new Thread(() =>
        {
            try
            {
                _avx2Stress.WarmUpJit();
                _avx512Stress.WarmUpJit();
            }
            catch
            {
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "StressWarmup"
        };

        thread.Start();
    }

    private void RunStressOnCore(int coreIndex, string algorithm, CancellationToken token)
    {
        // Lower thread priority so stress tests don't starve the UI thread
        var previousPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

        // Use LibreHardwareMonitor's ThreadAffinity for proper processor group handling
        var previousAffinity = SetThreadAffinity(coreIndex);

        try
        {
            switch (algorithm)
            {
                case "AVX2 1T":
                case "AVX2 nT":
                    RunAvx2Stress(coreIndex, token);
                    break;
                case "AVX2 Compute 1T":
                case "AVX2 Compute nT":
                    RunAvx2ComputeHot(coreIndex, token);
                    break;
                case "AVX2 FP64 1T":
                case "AVX2 FP64 nT":
                    RunAvx2Fp64(coreIndex, token);
                    break;
                case "AVX512 1T":
                case "AVX512 nT":
                    RunAvx512Stress(coreIndex, token);
                    break;
                case "AVX512 Compute 1T":
                case "AVX512 Compute nT":
                    RunAvx512ComputeHot(coreIndex, token);
                    break;
                case "AVX512 FP64 1T":
                case "AVX512 FP64 nT":
                    RunAvx512Fp64(coreIndex, token);
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

            // Restore thread priority
            Thread.CurrentThread.Priority = previousPriority;
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
        try
        {
            _avx2Stress.RunStress(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private void RunAvx2ComputeHot(int coreIndex, CancellationToken token)
    {
        try
        {
            _avx2Stress.RunComputeHotOnly(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private void RunAvx2Fp64(int coreIndex, CancellationToken token)
    {
        try
        {
            _avx2Stress.RunFp64ComputeHotOnly(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private void RunAvx512Stress(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
        {
            var errorMsg = $"Thread {coreIndex}: AVX-512 not supported on this CPU";
            _progressSubject.OnNext(new StressTestProgress(
                coreIndex,
                Environment.ProcessorCount,
                true,
                errorMsg,
                _totalErrorCount,
                errorMsg));
            return;
        }

        try
        {
            _avx512Stress.RunStress(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private void RunAvx512ComputeHot(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
        {
            var errorMsg = $"Thread {coreIndex}: AVX-512 not supported on this CPU";
            _progressSubject.OnNext(new StressTestProgress(
                coreIndex,
                Environment.ProcessorCount,
                true,
                errorMsg,
                _totalErrorCount,
                errorMsg));
            return;
        }

        try
        {
            _avx512Stress.RunComputeHotOnly(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private void RunAvx512Fp64(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
        {
            var errorMsg = $"Thread {coreIndex}: AVX-512 not supported on this CPU";
            _progressSubject.OnNext(new StressTestProgress(
                coreIndex,
                Environment.ProcessorCount,
                true,
                errorMsg,
                _totalErrorCount,
                errorMsg));
            return;
        }

        try
        {
            _avx512Stress.RunFp64ComputeHotOnly(coreIndex, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
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

    private unsafe void RunMemoryChaseStress(int coreIndex, CancellationToken token)
    {
        // Allocate buffer >> L3 cache size to ensure cache misses
        int elementCount = MemoryChaseBufferSize / sizeof(long);
        var buffer = new long[elementCount];

        // Initialize as shuffled pointer-chase chain
        // Each element contains the index of the next element to visit
        var indices = new int[elementCount];
        for (int i = 0; i < elementCount; i++)
            indices[i] = i;

        // Fisher-Yates shuffle to create random chase pattern
        for (int i = elementCount - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        // Build the pointer chain: buffer[indices[i]] points to indices[i+1]
        for (int i = 0; i < elementCount - 1; i++)
        {
            buffer[indices[i]] = indices[i + 1];
        }
        buffer[indices[elementCount - 1]] = indices[0]; // Complete the cycle

        try
        {
            long index = indices[0];
            long accumulator = 0;

            while (!token.IsCancellationRequested)
            {
                // Memory stress: follow N pointer hops (causes cache misses)
                for (int hop = 0; hop < MemoryChaseHopsPerBurst; hop++)
                {
                    index = buffer[index];
                    // CPU stress: do some arithmetic on loaded values
                    accumulator += index;
                    accumulator ^= (accumulator << 13);
                    accumulator ^= (accumulator >> 7);
                    accumulator ^= (accumulator << 17);
                }

                // Brief sleep for duty cycle (moderate load, not 100%)
                Thread.Sleep(MemoryChaseSleepMs);

                token.ThrowIfCancellationRequested();
            }

            // Use accumulator to prevent compiler optimization
            GC.KeepAlive(accumulator);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation - expected when test duration elapses
        }
        catch (Exception ex)
        {
            // Hardware fault or other critical error
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

        var algorithm = settings.SelectedAlgorithm;

        // Check AVX512 support before starting
        if (algorithm.StartsWith("AVX512", StringComparison.OrdinalIgnoreCase) && !Avx512F.IsSupported)
        {
            _progressSubject.OnNext(new StressTestProgress(0, coreData.ThreadCount, false,
                "AVX-512 is not supported on this CPU. Please select an AVX2 algorithm.",
                LastError: "AVX-512 not supported"));
            return;
        }

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

    public void Dispose()
    {
        _avx2Stress.Dispose();
        _avx512Stress.Dispose();
        _progressSubject.Dispose();
    }
}
