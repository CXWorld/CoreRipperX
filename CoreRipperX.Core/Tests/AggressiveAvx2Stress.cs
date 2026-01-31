using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CoreRipperX.Core.Tests;

public class AggressiveAvx2Stress : IDisposable
{
    private const int BufferSize = 32 * 1024 * 1024; // 32 MB - exceeds L3 cache
    private const int VectorCount = BufferSize / 32; // 32 bytes per Vector256

    // Per-thread buffers to avoid false sharing
    private readonly ThreadLocal<AlignedBuffer> _threadBuffers;

    public AggressiveAvx2Stress()
    {
        _threadBuffers = new ThreadLocal<AlignedBuffer>(
            () => new AlignedBuffer(BufferSize),
            trackAllValues: true);
    }

    public void RunStress(int coreIndex, CancellationToken token)
    {
        var buffer = _threadBuffers.Value!;
        var spanFloat = buffer.AsSpan<float>();
        var spanInt = buffer.AsSpan<int>();

        // Initialize with non-zero data
        for (int i = 0; i < spanFloat.Length; i++)
        {
            spanFloat[i] = 1.0f + (i & 0xFF) * 0.001f;
        }

        // Constants for FMA - values chosen to stay in normalized float range
        var mul1 = Vector256.Create(1.000001f);
        var mul2 = Vector256.Create(0.999999f);
        var add1 = Vector256.Create(0.0000001f);
        var add2 = Vector256.Create(-0.0000001f);

        // Integer constants for mixed ops
        var intMask = Vector256.Create(0x7FFFFFFF);
        var intAdd = Vector256.Create(1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                // === PHASE 1: Memory + Compute Stress ===
                StressWithMemory(spanFloat, mul1, mul2, add1, add2);

                // === PHASE 2: Pure Register Stress (no memory) ===
                StressPureCompute(mul1, mul2, add1, add2);

                // === PHASE 3: Mixed Int/Float Stress ===
                StressMixedIntFloat(spanFloat, spanInt, intMask, intAdd, mul1, add1);

                // === PHASE 4: Shuffle/Permute Stress ===
                StressShufflePermute(spanFloat);

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Prevent over-optimization
    private static void StressWithMemory(
        Span<float> data,
        Vector256<float> mul1, Vector256<float> mul2,
        Vector256<float> add1, Vector256<float> add2)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Process in chunks, streaming through memory
        for (int outer = 0; outer < 4; outer++)
        {
            for (int i = 0; i <= length - 64; i += 64) // 64 floats = 8 vectors per iteration
            {
                ref var ptr = ref Unsafe.Add(ref baseRef, i);

                // Load 8 vectors
                var v0 = Vector256.LoadUnsafe(ref ptr);
                var v1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 8));
                var v2 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
                var v3 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 24));
                var v4 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
                var v5 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 40));
                var v6 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));
                var v7 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 56));

                // Heavy FMA chain - 4 ops per vector
                v0 = Fma.MultiplyAdd(v0, mul1, add1);
                v1 = Fma.MultiplyAdd(v1, mul2, add2);
                v2 = Fma.MultiplyAdd(v2, mul1, add2);
                v3 = Fma.MultiplyAdd(v3, mul2, add1);
                v4 = Fma.MultiplyAdd(v4, mul1, add1);
                v5 = Fma.MultiplyAdd(v5, mul2, add2);
                v6 = Fma.MultiplyAdd(v6, mul1, add2);
                v7 = Fma.MultiplyAdd(v7, mul2, add1);

                v0 = Fma.MultiplyAddNegated(v0, mul2, v1);
                v2 = Fma.MultiplyAddNegated(v2, mul1, v3);
                v4 = Fma.MultiplyAddNegated(v4, mul2, v5);
                v6 = Fma.MultiplyAddNegated(v6, mul1, v7);

                v1 = Fma.MultiplySubtract(v1, mul1, add1);
                v3 = Fma.MultiplySubtract(v3, mul2, add2);
                v5 = Fma.MultiplySubtract(v5, mul1, add1);
                v7 = Fma.MultiplySubtract(v7, mul2, add2);

                v0 = Fma.MultiplyAdd(v0, mul1, v2);
                v4 = Fma.MultiplyAdd(v4, mul1, v6);
                v1 = Fma.MultiplyAdd(v1, mul2, v3);
                v5 = Fma.MultiplyAdd(v5, mul2, v7);

                // Store back
                v0.StoreUnsafe(ref ptr);
                v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 8));
                v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
                v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 24));
                v4.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
                v5.StoreUnsafe(ref Unsafe.Add(ref ptr, 40));
                v6.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
                v7.StoreUnsafe(ref Unsafe.Add(ref ptr, 56));
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressPureCompute(
        Vector256<float> mul1, Vector256<float> mul2,
        Vector256<float> add1, Vector256<float> add2)
    {
        // 12 independent chains for max ILP
        var a0 = Vector256.Create(1.0f);
        var a1 = Vector256.Create(1.1f);
        var a2 = Vector256.Create(1.2f);
        var a3 = Vector256.Create(1.3f);
        var a4 = Vector256.Create(1.4f);
        var a5 = Vector256.Create(1.5f);
        var a6 = Vector256.Create(1.6f);
        var a7 = Vector256.Create(1.7f);
        var a8 = Vector256.Create(1.8f);
        var a9 = Vector256.Create(1.9f);
        var aA = Vector256.Create(2.0f);
        var aB = Vector256.Create(2.1f);

        for (int i = 0; i < 100_000; i++)
        {
            // Mix of FMA variants to use different micro-ops
            a0 = Fma.MultiplyAdd(a0, mul1, add1);
            a1 = Fma.MultiplyAdd(a1, mul2, add2);
            a2 = Fma.MultiplySubtract(a2, mul1, add1);
            a3 = Fma.MultiplySubtract(a3, mul2, add2);
            a4 = Fma.MultiplyAddNegated(a4, mul1, add1);
            a5 = Fma.MultiplyAddNegated(a5, mul2, add2);
            a6 = Fma.MultiplySubtractNegated(a6, mul1, add1);
            a7 = Fma.MultiplySubtractNegated(a7, mul2, add2);
            a8 = Fma.MultiplyAdd(a8, mul1, a0);
            a9 = Fma.MultiplyAdd(a9, mul2, a1);
            aA = Fma.MultiplyAdd(aA, mul1, a2);
            aB = Fma.MultiplyAdd(aB, mul2, a3);

            // Extra multiply/add to stress more ports
            a0 = Avx.Multiply(a0, mul2);
            a1 = Avx.Add(a1, add1);
            a2 = Avx.Multiply(a2, mul1);
            a3 = Avx.Add(a3, add2);
        }

        // Consume results (prevent DCE)
        if (Avx.MoveMask(Avx.Compare(a0, a0, FloatComparisonMode.UnorderedNonSignaling)) != 0)
            throw new Exception("Computation error");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressMixedIntFloat(
        Span<float> floatData, Span<int> intData,
        Vector256<int> intMask, Vector256<int> intAdd,
        Vector256<float> mul, Vector256<float> add)
    {
        ref var floatRef = ref MemoryMarshal.GetReference(floatData);
        ref var intRef = ref MemoryMarshal.GetReference(intData);
        int length = Math.Min(floatData.Length, intData.Length);

        // Mixed int/float stresses different execution ports simultaneously
        for (int i = 0; i <= length - 32; i += 32)
        {
            ref var fptr = ref Unsafe.Add(ref floatRef, i);
            ref var iptr = ref Unsafe.Add(ref intRef, i);

            // Float operations
            var f0 = Vector256.LoadUnsafe(ref fptr);
            var f1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref fptr, 8));
            var f2 = Vector256.LoadUnsafe(ref Unsafe.Add(ref fptr, 16));
            var f3 = Vector256.LoadUnsafe(ref Unsafe.Add(ref fptr, 24));

            // Integer operations (same memory, reinterpreted)
            var i0 = Vector256.LoadUnsafe(ref iptr);
            var i1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref iptr, 8));
            var i2 = Vector256.LoadUnsafe(ref Unsafe.Add(ref iptr, 16));
            var i3 = Vector256.LoadUnsafe(ref Unsafe.Add(ref iptr, 24));

            // Float compute
            f0 = Fma.MultiplyAdd(f0, mul, add);
            f1 = Fma.MultiplyAdd(f1, mul, add);
            f2 = Fma.MultiplyAdd(f2, mul, add);
            f3 = Fma.MultiplyAdd(f3, mul, add);

            // Integer compute (parallel on different ports)
            i0 = Avx2.Add(Avx2.And(i0, intMask), intAdd);
            i1 = Avx2.Add(Avx2.And(i1, intMask), intAdd);
            i2 = Avx2.Add(Avx2.And(i2, intMask), intAdd);
            i3 = Avx2.Add(Avx2.And(i3, intMask), intAdd);

            // More float
            f0 = Avx.Sqrt(Avx.Max(f0, Vector256.Create(0.0001f)));
            f1 = Avx.Sqrt(Avx.Max(f1, Vector256.Create(0.0001f)));

            // Integer multiply (expensive)
            i0 = Avx2.MultiplyLow(i0, i1);
            i2 = Avx2.MultiplyLow(i2, i3);

            // Store
            f0.StoreUnsafe(ref fptr);
            f1.StoreUnsafe(ref Unsafe.Add(ref fptr, 8));
            i0.StoreUnsafe(ref iptr);
            i2.StoreUnsafe(ref Unsafe.Add(ref iptr, 16));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressShufflePermute(Span<float> data)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Shuffle/permute stress - uses different execution port than FMA
        for (int i = 0; i <= length - 32; i += 32)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Vector256.LoadUnsafe(ref ptr);
            var v1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 8));
            var v2 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
            var v3 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ptr, 24));

            // Permutes
            v0 = Avx2.Permute4x64(v0.AsDouble(), 0b10_11_00_01).AsSingle();
            v1 = Avx2.Permute4x64(v1.AsDouble(), 0b01_00_11_10).AsSingle();

            // Shuffles
            v2 = Avx.Shuffle(v2, v3, 0b10_01_10_01);
            v3 = Avx.Shuffle(v3, v2, 0b01_10_01_10);

            // Blends
            v0 = Avx.Blend(v0, v1, 0b10101010);
            v2 = Avx.Blend(v2, v3, 0b01010101);

            // Cross-lane permute (expensive)
            var idx = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
            v1 = Avx2.PermuteVar8x32(v1, idx);
            v3 = Avx2.PermuteVar8x32(v3, idx);

            v0.StoreUnsafe(ref ptr);
            v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 8));
            v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
            v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 24));
        }
    }

    public void Dispose()
    {
        foreach (var buffer in _threadBuffers.Values)
            buffer.Dispose();
        _threadBuffers.Dispose();
    }
}

// Aligned buffer for optimal SIMD access
public sealed class AlignedBuffer : IDisposable
{
    private readonly unsafe void* _ptr;
    private readonly int _size;

    public unsafe AlignedBuffer(int size)
    {
        _size = size;
        _ptr = NativeMemory.AlignedAlloc((nuint)size, 64); // 64-byte alignment for cache lines
        NativeMemory.Clear(_ptr, (nuint)size);
    }

    public unsafe Span<T> AsSpan<T>() where T : unmanaged
        => new(_ptr, _size / sizeof(T));

    public unsafe void Dispose()
        => NativeMemory.AlignedFree(_ptr);
}