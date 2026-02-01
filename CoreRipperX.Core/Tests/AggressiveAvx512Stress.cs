using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CoreRipperX.Core.Tests;

public class AggressiveAvx512Stress : IDisposable
{
    private const int BufferSize = 64 * 1024 * 1024; // 64 MB - larger for 512-bit ops

    private readonly ThreadLocal<AlignedBuffer> _threadBuffers;
    private int _jitWarmed;

    public AggressiveAvx512Stress()
    {
        _threadBuffers = new ThreadLocal<AlignedBuffer>(
            () => new AlignedBuffer(BufferSize),
            trackAllValues: true);
    }

    public void WarmUpJit()
    {
        if (!Avx512F.IsSupported)
            return;

        if (Interlocked.Exchange(ref _jitWarmed, 1) == 1)
            return;

        var mul1 = Vector512.Create(1.0000001f);
        var mul2 = Vector512.Create(0.9999999f);
        var add1 = Vector512.Create(0.00000001f);
        var add2 = Vector512.Create(-0.00000001f);

        var dMul1 = Vector512.Create(1.000000000001);
        var dMul2 = Vector512.Create(0.999999999999);
        var dAdd1 = Vector512.Create(0.0000000000001);
        var dAdd2 = Vector512.Create(-0.0000000000001);

        StressComputeHot(mul1, mul2, add1, add2, CancellationToken.None);
        StressComputeHotDouble(dMul1, dMul2, dAdd1, dAdd2, CancellationToken.None);
    }

    public void RunMixedStress(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
        {
            throw new PlatformNotSupportedException("AVX-512F not supported");
        }

        var buffer = _threadBuffers.Value!;
        var spanFloat = buffer.AsSpan<float>();
        var spanInt = buffer.AsSpan<int>();

        // Initialize with non-zero data
        for (int i = 0; i < spanFloat.Length; i++)
        {
            spanFloat[i] = 1.0f + (i & 0xFF) * 0.001f;
        }

        // Constants for FMA - values chosen to stay in normalized float range
        var mul1 = Vector512.Create(1.000001f);
        var mul2 = Vector512.Create(0.999999f);
        var add1 = Vector512.Create(0.0000001f);
        var add2 = Vector512.Create(-0.0000001f);

        // Integer constants for mixed ops
        var intMask = Vector512.Create(0x7FFFFFFF);
        var intAdd = Vector512.Create(1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                // === PHASE 0: Hot compute-only FMA saturation ===
                StressComputeHot(mul1, mul2, add1, add2, token);

                // === PHASE 1: Memory + Compute Stress ===
                StressMemoryCompute(spanFloat, mul1, mul2, add1, add2);

                // === PHASE 2: Mixed Int/Float Stress ===
                StressMixedIntFloat(spanFloat, spanInt, intMask, intAdd, mul1, add1);

                // === PHASE 3: Shuffle/Permute Stress ===
                StressShufflePermute(spanFloat);

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void RunComputeHotOnly(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
            throw new PlatformNotSupportedException("AVX-512F not supported");

        var mul1 = Vector512.Create(1.0000001f);
        var mul2 = Vector512.Create(0.9999999f);
        var add1 = Vector512.Create(0.00000001f);
        var add2 = Vector512.Create(-0.00000001f);

        try
        {
            while (!token.IsCancellationRequested)
            {
                StressComputeHot(mul1, mul2, add1, add2, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void RunFp64ComputeHotOnly(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
            throw new PlatformNotSupportedException("AVX-512F not supported");

        var mul1 = Vector512.Create(1.000000000001);
        var mul2 = Vector512.Create(0.999999999999);
        var add1 = Vector512.Create(0.0000000000001);
        var add2 = Vector512.Create(-0.0000000000001);

        try
        {
            while (!token.IsCancellationRequested)
            {
                StressComputeHotDouble(mul1, mul2, add1, add2, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressComputeHotDouble(
        Vector512<double> mul1, Vector512<double> mul2,
        Vector512<double> add1, Vector512<double> add2,
        CancellationToken token)
    {
        // 12 chains (8 doubles per vector) to keep FMA units saturated
        var a00 = Vector512.Create(1.000);
        var a01 = Vector512.Create(1.050);
        var a02 = Vector512.Create(1.100);
        var a03 = Vector512.Create(1.150);
        var a04 = Vector512.Create(1.200);
        var a05 = Vector512.Create(1.250);
        var a06 = Vector512.Create(1.300);
        var a07 = Vector512.Create(1.350);
        var a08 = Vector512.Create(1.400);
        var a09 = Vector512.Create(1.450);
        var a10 = Vector512.Create(1.500);
        var a11 = Vector512.Create(1.550);

        for (int i = 0; i < 200_000; i++)
        {
            a00 = Avx512F.FusedMultiplyAdd(a00, mul1, add1);
            a01 = Avx512F.FusedMultiplyAdd(a01, mul2, add2);
            a02 = Avx512F.FusedMultiplySubtract(a02, mul1, add1);
            a03 = Avx512F.FusedMultiplySubtract(a03, mul2, add2);
            a04 = Avx512F.FusedMultiplyAddNegated(a04, mul1, add1);
            a05 = Avx512F.FusedMultiplyAddNegated(a05, mul2, add2);
            a06 = Avx512F.FusedMultiplySubtractNegated(a06, mul1, add1);
            a07 = Avx512F.FusedMultiplySubtractNegated(a07, mul2, add2);
            a08 = Avx512F.FusedMultiplyAdd(a08, mul1, a00);
            a09 = Avx512F.FusedMultiplyAdd(a09, mul2, a01);
            a10 = Avx512F.FusedMultiplyAdd(a10, mul1, a02);
            a11 = Avx512F.FusedMultiplyAdd(a11, mul2, a03);

            a00 = Avx512F.Multiply(a00, mul2);
            a01 = Avx512F.Add(a01, add1);
            a02 = Avx512F.Multiply(a02, mul1);
            a03 = Avx512F.Add(a03, add2);
            a04 = Avx512F.Subtract(a04, add1);
            a05 = Avx512F.Subtract(a05, add2);

            if ((i & 0xF) == 0)
                Thread.Yield();

            if ((i & 0x3FFF) == 0 && token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
        }

        var sum = Avx512F.Add(a00, a01);
        sum = Avx512F.Add(sum, a02); sum = Avx512F.Add(sum, a03);
        sum = Avx512F.Add(sum, a04); sum = Avx512F.Add(sum, a05);
        sum = Avx512F.Add(sum, a06); sum = Avx512F.Add(sum, a07);
        sum = Avx512F.Add(sum, a08); sum = Avx512F.Add(sum, a09);
        sum = Avx512F.Add(sum, a10); sum = Avx512F.Add(sum, a11);

        var nanCheck = Avx512F.CompareEqual(sum, sum);
        if (nanCheck.AsInt64() != Vector512<long>.AllBitsSet)
            throw new Exception("AVX-512 computation error detected");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressComputeHot(
        Vector512<float> mul1, Vector512<float> mul2,
        Vector512<float> add1, Vector512<float> add2,
        CancellationToken token)
    {
        // 24 independent chains to saturate ZMM/FMA units with minimal memory traffic
        var a00 = Vector512.Create(1.00f);
        var a01 = Vector512.Create(1.03f);
        var a02 = Vector512.Create(1.06f);
        var a03 = Vector512.Create(1.09f);
        var a04 = Vector512.Create(1.12f);
        var a05 = Vector512.Create(1.15f);
        var a06 = Vector512.Create(1.18f);
        var a07 = Vector512.Create(1.21f);
        var a08 = Vector512.Create(1.24f);
        var a09 = Vector512.Create(1.27f);
        var a10 = Vector512.Create(1.30f);
        var a11 = Vector512.Create(1.33f);
        var a12 = Vector512.Create(1.36f);
        var a13 = Vector512.Create(1.39f);
        var a14 = Vector512.Create(1.42f);
        var a15 = Vector512.Create(1.45f);
        var a16 = Vector512.Create(1.48f);
        var a17 = Vector512.Create(1.51f);
        var a18 = Vector512.Create(1.54f);
        var a19 = Vector512.Create(1.57f);
        var a20 = Vector512.Create(1.60f);
        var a21 = Vector512.Create(1.63f);
        var a22 = Vector512.Create(1.66f);
        var a23 = Vector512.Create(1.69f);

        for (int i = 0; i < 220_000; i++)
        {
            a00 = Avx512F.FusedMultiplyAdd(a00, mul1, add1);
            a01 = Avx512F.FusedMultiplyAdd(a01, mul2, add2);
            a02 = Avx512F.FusedMultiplySubtract(a02, mul1, add1);
            a03 = Avx512F.FusedMultiplySubtract(a03, mul2, add2);
            a04 = Avx512F.FusedMultiplyAddNegated(a04, mul1, add1);
            a05 = Avx512F.FusedMultiplyAddNegated(a05, mul2, add2);
            a06 = Avx512F.FusedMultiplySubtractNegated(a06, mul1, add1);
            a07 = Avx512F.FusedMultiplySubtractNegated(a07, mul2, add2);
            a08 = Avx512F.FusedMultiplyAdd(a08, mul1, a00);
            a09 = Avx512F.FusedMultiplyAdd(a09, mul2, a01);
            a10 = Avx512F.FusedMultiplyAdd(a10, mul1, a02);
            a11 = Avx512F.FusedMultiplyAdd(a11, mul2, a03);
            a12 = Avx512F.FusedMultiplyAdd(a12, mul1, a04);
            a13 = Avx512F.FusedMultiplyAdd(a13, mul2, a05);
            a14 = Avx512F.FusedMultiplyAdd(a14, mul1, a06);
            a15 = Avx512F.FusedMultiplyAdd(a15, mul2, a07);
            a16 = Avx512F.FusedMultiplyAdd(a16, mul1, a08);
            a17 = Avx512F.FusedMultiplyAdd(a17, mul2, a09);
            a18 = Avx512F.FusedMultiplyAdd(a18, mul1, a10);
            a19 = Avx512F.FusedMultiplyAdd(a19, mul2, a11);
            a20 = Avx512F.FusedMultiplyAdd(a20, mul1, a12);
            a21 = Avx512F.FusedMultiplyAdd(a21, mul2, a13);
            a22 = Avx512F.FusedMultiplyAdd(a22, mul1, a14);
            a23 = Avx512F.FusedMultiplyAdd(a23, mul2, a15);

            a00 = Avx512F.Multiply(a00, mul2);
            a01 = Avx512F.Add(a01, add1);
            a02 = Avx512F.Multiply(a02, mul1);
            a03 = Avx512F.Add(a03, add2);
            a04 = Avx512F.Subtract(a04, add1);
            a05 = Avx512F.Subtract(a05, add2);

            if ((i & 0xF) == 0)
                Thread.Yield();

            if ((i & 0x3FFF) == 0 && token.IsCancellationRequested)
                token.ThrowIfCancellationRequested();
        }

        // Prevent DCE
        var sum = Avx512F.Add(a00, a01);
        sum = Avx512F.Add(sum, a02); sum = Avx512F.Add(sum, a03);
        sum = Avx512F.Add(sum, a04); sum = Avx512F.Add(sum, a05);
        sum = Avx512F.Add(sum, a06); sum = Avx512F.Add(sum, a07);
        sum = Avx512F.Add(sum, a08); sum = Avx512F.Add(sum, a09);
        sum = Avx512F.Add(sum, a10); sum = Avx512F.Add(sum, a11);
        sum = Avx512F.Add(sum, a12); sum = Avx512F.Add(sum, a13);
        sum = Avx512F.Add(sum, a14); sum = Avx512F.Add(sum, a15);
        sum = Avx512F.Add(sum, a16); sum = Avx512F.Add(sum, a17);
        sum = Avx512F.Add(sum, a18); sum = Avx512F.Add(sum, a19);
        sum = Avx512F.Add(sum, a20); sum = Avx512F.Add(sum, a21);
        sum = Avx512F.Add(sum, a22); sum = Avx512F.Add(sum, a23);

        var nanCheck = Avx512F.CompareEqual(sum, sum);
        if (nanCheck.AsInt32() != Vector512<int>.AllBitsSet)
            throw new Exception("AVX-512 computation error detected");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressMemoryCompute(
        Span<float> data,
        Vector512<float> mul1, Vector512<float> mul2,
        Vector512<float> add1, Vector512<float> add2)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        for (int outer = 0; outer < 4; outer++)
        {
            for (int i = 0; i <= length - 128; i += 128) // 128 floats = 8 vectors
            {
                ref var ptr = ref Unsafe.Add(ref baseRef, i);

                // Load 8 x 512-bit vectors
                var v0 = Vector512.LoadUnsafe(ref ptr);
                var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
                var v2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
                var v3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));
                var v4 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 64));
                var v5 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 80));
                var v6 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 96));
                var v7 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 112));

                // Round 1: FMA
                v0 = Avx512F.FusedMultiplyAdd(v0, mul1, add1);
                v1 = Avx512F.FusedMultiplyAdd(v1, mul2, add2);
                v2 = Avx512F.FusedMultiplyAdd(v2, mul1, add2);
                v3 = Avx512F.FusedMultiplyAdd(v3, mul2, add1);
                v4 = Avx512F.FusedMultiplyAdd(v4, mul1, add1);
                v5 = Avx512F.FusedMultiplyAdd(v5, mul2, add2);
                v6 = Avx512F.FusedMultiplyAdd(v6, mul1, add2);
                v7 = Avx512F.FusedMultiplyAdd(v7, mul2, add1);

                for (int round = 0; round < 3; round++)
                {
                    // Round 2: FMA Negated variants
                    v0 = Avx512F.FusedMultiplyAddNegated(v0, mul2, v1);
                    v2 = Avx512F.FusedMultiplyAddNegated(v2, mul1, v3);
                    v4 = Avx512F.FusedMultiplyAddNegated(v4, mul2, v5);
                    v6 = Avx512F.FusedMultiplyAddNegated(v6, mul1, v7);

                    // Round 3: FMA Subtract
                    v1 = Avx512F.FusedMultiplySubtract(v1, mul1, add1);
                    v3 = Avx512F.FusedMultiplySubtract(v3, mul2, add2);
                    v5 = Avx512F.FusedMultiplySubtract(v5, mul1, add1);
                    v7 = Avx512F.FusedMultiplySubtract(v7, mul2, add2);

                    // Round 4: Cross-dependencies
                    v0 = Avx512F.FusedMultiplyAdd(v0, mul1, v4);
                    v1 = Avx512F.FusedMultiplyAdd(v1, mul2, v5);
                    v2 = Avx512F.FusedMultiplyAdd(v2, mul1, v6);
                    v3 = Avx512F.FusedMultiplyAdd(v3, mul2, v7);

                    // Round 5: More FMA to increase compute density
                    v4 = Avx512F.FusedMultiplyAdd(v4, mul1, v0);
                    v5 = Avx512F.FusedMultiplyAdd(v5, mul2, v1);
                    v6 = Avx512F.FusedMultiplyAdd(v6, mul1, v2);
                    v7 = Avx512F.FusedMultiplyAdd(v7, mul2, v3);
                }

                // Store
                v0.StoreUnsafe(ref ptr);
                v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
                v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
                v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
                v4.StoreUnsafe(ref Unsafe.Add(ref ptr, 64));
                v5.StoreUnsafe(ref Unsafe.Add(ref ptr, 80));
                v6.StoreUnsafe(ref Unsafe.Add(ref ptr, 96));
                v7.StoreUnsafe(ref Unsafe.Add(ref ptr, 112));
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressMixedIntFloat(
        Span<float> floatData, Span<int> intData,
        Vector512<int> intMask, Vector512<int> intAdd,
        Vector512<float> mul, Vector512<float> add)
    {
        ref var floatRef = ref MemoryMarshal.GetReference(floatData);
        ref var intRef = ref MemoryMarshal.GetReference(intData);
        int length = Math.Min(floatData.Length, intData.Length);

        // Mixed int/float stresses different execution ports simultaneously
        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var fptr = ref Unsafe.Add(ref floatRef, i);
            ref var iptr = ref Unsafe.Add(ref intRef, i);

            // Float operations
            var f0 = Vector512.LoadUnsafe(ref fptr);
            var f1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 16));
            var f2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 32));
            var f3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 48));

            // Integer operations (same memory, reinterpreted)
            var i0 = Vector512.LoadUnsafe(ref iptr);
            var i1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 16));
            var i2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 32));
            var i3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 48));

            // Float compute
            f0 = Avx512F.FusedMultiplyAdd(f0, mul, add);
            f1 = Avx512F.FusedMultiplyAdd(f1, mul, add);
            f2 = Avx512F.FusedMultiplyAdd(f2, mul, add);
            f3 = Avx512F.FusedMultiplyAdd(f3, mul, add);

            // Integer compute (parallel on different ports)
            i0 = Avx512F.Add(Avx512F.And(i0, intMask), intAdd);
            i1 = Avx512F.Add(Avx512F.And(i1, intMask), intAdd);
            i2 = Avx512F.Add(Avx512F.And(i2, intMask), intAdd);
            i3 = Avx512F.Add(Avx512F.And(i3, intMask), intAdd);

            // Integer multiply (expensive)
            i0 = Avx512F.MultiplyLow(i0, i1);
            i2 = Avx512F.MultiplyLow(i2, i3);

            // Store
            f0.StoreUnsafe(ref fptr);
            f1.StoreUnsafe(ref Unsafe.Add(ref fptr, 16));
            f2.StoreUnsafe(ref Unsafe.Add(ref fptr, 32));
            f3.StoreUnsafe(ref Unsafe.Add(ref fptr, 48));
            i0.StoreUnsafe(ref iptr);
            i1.StoreUnsafe(ref Unsafe.Add(ref iptr, 16));
            i2.StoreUnsafe(ref Unsafe.Add(ref iptr, 32));
            i3.StoreUnsafe(ref Unsafe.Add(ref iptr, 48));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressShufflePermute(Span<float> data)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Shuffle/permute stress - uses different execution port than FMA
        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Vector512.LoadUnsafe(ref ptr);
            var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
            var v2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
            var v3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));

            // Permutes
            v0 = Avx512F.PermuteVar8x64(v0.AsDouble(), Vector512.Create(2L, 3L, 0L, 1L, 6L, 7L, 4L, 5L)).AsSingle();
            v1 = Avx512F.PermuteVar8x64(v1.AsDouble(), Vector512.Create(1L, 0L, 3L, 2L, 5L, 4L, 7L, 6L)).AsSingle();

            // Shuffles
            v2 = Avx512F.Shuffle(v2, v3, 0b10_01_10_01);
            v3 = Avx512F.Shuffle(v3, v2, 0b01_10_01_10);

            // Blends
            v0 = Avx512F.BlendVariable(v0, v1, Vector512.Create(-1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0).AsSingle());
            v2 = Avx512F.BlendVariable(v2, v3, Vector512.Create(0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1).AsSingle());

            // Cross-lane permute (expensive)
            var idx = Vector512.Create(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0);
            v1 = Avx512F.PermuteVar16x32(v1, idx);
            v3 = Avx512F.PermuteVar16x32(v3, idx);

            v0.StoreUnsafe(ref ptr);
            v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
            v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
            v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
        }
    }

    public void Dispose()
    {
        foreach (var buffer in _threadBuffers.Values)
            buffer.Dispose();
        _threadBuffers.Dispose();
    }
}
