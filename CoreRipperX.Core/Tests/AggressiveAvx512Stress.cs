using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CoreRipperX.Core.Tests;

public class AggressiveAvx512Stress : IDisposable
{
    private const int BufferSize = 64 * 1024 * 1024; // 64 MB - larger for 512-bit ops
    private const int VectorCount = BufferSize / 64; // 64 bytes per Vector512

    private readonly ThreadLocal<AlignedBuffer> _threadBuffers;

    public AggressiveAvx512Stress()
    {
        _threadBuffers = new ThreadLocal<AlignedBuffer>(
            () => new AlignedBuffer(BufferSize),
            trackAllValues: true);
    }

    public void RunStress(int coreIndex, CancellationToken token)
    {
        if (!Avx512F.IsSupported)
        {
            throw new PlatformNotSupportedException("AVX-512F not supported");
        }

        var buffer = _threadBuffers.Value!;
        var spanFloat = buffer.AsSpan<float>();
        var spanInt = buffer.AsSpan<int>();
        var spanLong = buffer.AsSpan<long>();

        // Initialize with non-trivial data
        for (int i = 0; i < spanFloat.Length; i++)
        {
            spanFloat[i] = 1.0f + (i & 0xFFF) * 0.0001f;
        }

        // Constants
        var mul1 = Vector512.Create(1.0000001f);
        var mul2 = Vector512.Create(0.9999999f);
        var add1 = Vector512.Create(0.00000001f);
        var add2 = Vector512.Create(-0.00000001f);

        var intMul = Vector512.Create(0x01010101);
        var intAdd = Vector512.Create(1);

        try
        {
            while (!token.IsCancellationRequested)
            {
                // === PHASE 1: Memory Bandwidth + Compute ===
                StressMemoryCompute(spanFloat, mul1, mul2, add1, add2);

                // === PHASE 2: Pure Register Pressure (20 accumulators) ===
                StressPureCompute(mul1, mul2, add1, add2);

                // === PHASE 3: Mixed Int/Float on Different Ports ===
                StressMixedIntFloat(spanFloat, spanInt, intMul, intAdd, mul1, add1);

                // === PHASE 4: Shuffle/Permute/Compress ===
                StressShufflePermute(spanFloat);

                // === PHASE 5: Additional FMA stress ===
                StressAdditionalFma(spanFloat, mul1, mul2, add1, add2);

                // === PHASE 6: Mask Operations ===
                StressMaskOps(spanFloat, mul1, add1);

                // === PHASE 7: Transcendental Approximations (expensive) ===
                StressTranscendental(spanFloat);

                token.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) { }
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
    private static void StressPureCompute(
        Vector512<float> mul1, Vector512<float> mul2,
        Vector512<float> add1, Vector512<float> add2)
    {
        // 20 independent chains - exploit 32 ZMM registers
        var a00 = Vector512.Create(1.00f);
        var a01 = Vector512.Create(1.05f);
        var a02 = Vector512.Create(1.10f);
        var a03 = Vector512.Create(1.15f);
        var a04 = Vector512.Create(1.20f);
        var a05 = Vector512.Create(1.25f);
        var a06 = Vector512.Create(1.30f);
        var a07 = Vector512.Create(1.35f);
        var a08 = Vector512.Create(1.40f);
        var a09 = Vector512.Create(1.45f);
        var a10 = Vector512.Create(1.50f);
        var a11 = Vector512.Create(1.55f);
        var a12 = Vector512.Create(1.60f);
        var a13 = Vector512.Create(1.65f);
        var a14 = Vector512.Create(1.70f);
        var a15 = Vector512.Create(1.75f);
        var a16 = Vector512.Create(1.80f);
        var a17 = Vector512.Create(1.85f);
        var a18 = Vector512.Create(1.90f);
        var a19 = Vector512.Create(1.95f);

        for (int i = 0; i < 80_000; i++)
        {
            // All FMA variants to hit different micro-ops
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

            // Extra ops on port 0/1
            a00 = Avx512F.Multiply(a00, mul2);
            a01 = Avx512F.Add(a01, add1);
            a02 = Avx512F.Multiply(a02, mul1);
            a03 = Avx512F.Add(a03, add2);
            a04 = Avx512F.Subtract(a04, add1);
            a05 = Avx512F.Subtract(a05, add2);
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

        // NaN check - CompareEqual returns all-ones for equal elements, NaN != NaN
        var nanCheck = Avx512F.CompareEqual(sum, sum);
        if (nanCheck != Vector512<float>.AllBitsSet)
            throw new Exception("AVX-512 computation error detected");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressMixedIntFloat(
        Span<float> floatData, Span<int> intData,
        Vector512<int> intMul, Vector512<int> intAdd,
        Vector512<float> fMul, Vector512<float> fAdd)
    {
        ref var floatRef = ref MemoryMarshal.GetReference(floatData);
        ref var intRef = ref MemoryMarshal.GetReference(intData);
        int length = Math.Min(floatData.Length, intData.Length);

        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var fptr = ref Unsafe.Add(ref floatRef, i);
            ref var iptr = ref Unsafe.Add(ref intRef, i);

            // Load float vectors
            var f0 = Vector512.LoadUnsafe(ref fptr);
            var f1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 16));
            var f2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 32));
            var f3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref fptr, 48));

            // Load int vectors
            var i0 = Vector512.LoadUnsafe(ref iptr);
            var i1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 16));
            var i2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 32));
            var i3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref iptr, 48));

            // Float FMA (port 0/1)
            f0 = Avx512F.FusedMultiplyAdd(f0, fMul, fAdd);
            f1 = Avx512F.FusedMultiplyAdd(f1, fMul, fAdd);
            f2 = Avx512F.FusedMultiplyAdd(f2, fMul, fAdd);
            f3 = Avx512F.FusedMultiplyAdd(f3, fMul, fAdd);

            // Integer ops (port 0/5)
            i0 = Avx512F.Add(i0, intAdd);
            i1 = Avx512F.Add(i1, intAdd);
            i2 = Avx512F.Add(i2, intAdd);
            i3 = Avx512F.Add(i3, intAdd);

            // Float sqrt (very expensive, port 0)
            f0 = Avx512F.Sqrt(Avx512F.Max(f0, Vector512.Create(0.0001f)));
            f1 = Avx512F.Sqrt(Avx512F.Max(f1, Vector512.Create(0.0001f)));

            // Integer multiply (expensive)
            i0 = Avx512F.MultiplyLow(i0, intMul);
            i1 = Avx512F.MultiplyLow(i1, intMul);

            // Float reciprocal sqrt (port 0)
            f2 = Avx512F.ReciprocalSqrt14(Avx512F.Max(f2, Vector512.Create(0.0001f)));
            f3 = Avx512F.ReciprocalSqrt14(Avx512F.Max(f3, Vector512.Create(0.0001f)));

            // Integer shift (port 0)
            i2 = Avx512F.ShiftLeftLogical(i2, 1);
            i3 = Avx512F.ShiftRightLogical(i3, 1);

            // Convert int<->float (stresses conversion units)
            var fi0 = Avx512F.ConvertToVector512Single(i0);
            var fi1 = Avx512F.ConvertToVector512Single(i1);
            f0 = Avx512F.Add(f0, fi0);
            f1 = Avx512F.Add(f1, fi1);

            var if2 = Avx512F.ConvertToVector512Int32(f2);
            var if3 = Avx512F.ConvertToVector512Int32(f3);
            i2 = Avx512F.Add(i2, if2);
            i3 = Avx512F.Add(i3, if3);

            // Store
            f0.StoreUnsafe(ref fptr);
            f1.StoreUnsafe(ref Unsafe.Add(ref fptr, 16));
            i0.StoreUnsafe(ref iptr);
            i1.StoreUnsafe(ref Unsafe.Add(ref iptr, 16));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressShufflePermute(Span<float> data)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Permute indices
        var idx1 = Vector512.Create(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0);
        var idx2 = Vector512.Create(0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15);
        var idx3 = Vector512.Create(8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7);

        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Vector512.LoadUnsafe(ref ptr);
            var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
            var v2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
            var v3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));

            // Full cross-lane permutes (expensive)
            v0 = Avx512F.PermuteVar16x32(v0, idx1);
            v1 = Avx512F.PermuteVar16x32(v1, idx2);
            v2 = Avx512F.PermuteVar16x32(v2, idx3);
            v3 = Avx512F.PermuteVar16x32(v3, idx1);

            // Two-source permute
            v0 = Avx512F.PermuteVar16x32x2(v0, idx2, v1);
            v2 = Avx512F.PermuteVar16x32x2(v2, idx3, v3);

            // Shuffle within 128-bit lanes
            v1 = Avx512F.Shuffle(v1, v0, 0b10_01_00_11);
            v3 = Avx512F.Shuffle(v3, v2, 0b01_00_11_10);

            // Alignr (byte shift across lanes)
            v0 = Avx512BW.IsSupported
                ? Avx512BW.AlignRight(v0.AsByte(), v1.AsByte(), 4).AsSingle()
                : Avx512F.PermuteVar16x32(v0, idx1);

            // Blend with mask
            v1 = Vector512.ConditionalSelect(
                Vector512.Create(-1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0).AsSingle(),
                v1, v2);

            // Store
            v0.StoreUnsafe(ref ptr);
            v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
            v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
            v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressAdditionalFma(
        Span<float> data,
        Vector512<float> mul1, Vector512<float> mul2,
        Vector512<float> add1, Vector512<float> add2)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Additional compute-heavy FMA stress with different patterns
        for (int i = 0; i <= length - 128; i += 128)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Vector512.LoadUnsafe(ref ptr);
            var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
            var v2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
            var v3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));
            var v4 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 64));
            var v5 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 80));
            var v6 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 96));
            var v7 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 112));

            // Chain of dependent FMAs
            v0 = Avx512F.FusedMultiplyAdd(v0, mul1, v1);
            v1 = Avx512F.FusedMultiplyAdd(v1, mul2, v2);
            v2 = Avx512F.FusedMultiplyAdd(v2, mul1, v3);
            v3 = Avx512F.FusedMultiplyAdd(v3, mul2, v4);
            v4 = Avx512F.FusedMultiplyAdd(v4, mul1, v5);
            v5 = Avx512F.FusedMultiplyAdd(v5, mul2, v6);
            v6 = Avx512F.FusedMultiplyAdd(v6, mul1, v7);
            v7 = Avx512F.FusedMultiplyAdd(v7, mul2, v0);

            // More FMA with subtract variants
            v0 = Avx512F.FusedMultiplySubtract(v0, mul2, add1);
            v1 = Avx512F.FusedMultiplySubtract(v1, mul1, add2);
            v2 = Avx512F.FusedMultiplySubtract(v2, mul2, add1);
            v3 = Avx512F.FusedMultiplySubtract(v3, mul1, add2);
            v4 = Avx512F.FusedMultiplyAddNegated(v4, mul2, add1);
            v5 = Avx512F.FusedMultiplyAddNegated(v5, mul1, add2);
            v6 = Avx512F.FusedMultiplySubtractNegated(v6, mul2, add1);
            v7 = Avx512F.FusedMultiplySubtractNegated(v7, mul1, add2);

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressMaskOps(
        Span<float> data,
        Vector512<float> mul, Vector512<float> add)
    {
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        var threshold = Vector512.Create(1.5f);
        var zero = Vector512<float>.Zero;

        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Vector512.LoadUnsafe(ref ptr);
            var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16));
            var v2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32));
            var v3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48));

            // Generate masks from comparisons (returns Vector512 with all-ones or all-zeros per element)
            var mask0 = Avx512F.CompareGreaterThan(v0, threshold);
            var mask1 = Avx512F.CompareLessThan(v1, threshold);
            var mask2 = Avx512F.CompareGreaterThanOrEqual(v2, threshold);
            var mask3 = Avx512F.CompareNotEqual(v3, zero);

            // Masked operations using BlendVariable
            v0 = Avx512F.BlendVariable(v0, Avx512F.FusedMultiplyAdd(v0, mul, add), mask0);
            v1 = Avx512F.BlendVariable(v1, Avx512F.FusedMultiplyAdd(v1, mul, add), mask1);

            // BlendVariable for v2 based on comparison
            var fmaV2 = Avx512F.FusedMultiplyAdd(v2, mul, add);
            v2 = Avx512F.BlendVariable(v2, fmaV2, mask2);

            // Masked FMA for v3
            v3 = Avx512F.BlendVariable(zero, Avx512F.FusedMultiplyAdd(v3, mul, add), mask3);

            // Store
            v0.StoreUnsafe(ref ptr);
            v1.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
            v2.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
            v3.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StressTranscendental(Span<float> data)
    {
        // Approximate transcendentals using polynomial (very compute heavy)
        ref var baseRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;

        // Polynomial coefficients for exp approximation
        var c0 = Vector512.Create(1.0f);
        var c1 = Vector512.Create(1.0f);
        var c2 = Vector512.Create(0.5f);
        var c3 = Vector512.Create(0.166666667f);
        var c4 = Vector512.Create(0.041666667f);
        var c5 = Vector512.Create(0.008333333f);

        var scale = Vector512.Create(0.01f); // Keep values small

        for (int i = 0; i <= length - 64; i += 64)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);

            var v0 = Avx512F.Multiply(Vector512.LoadUnsafe(ref ptr), scale);
            var v1 = Avx512F.Multiply(Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 16)), scale);
            var v2 = Avx512F.Multiply(Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 32)), scale);
            var v3 = Avx512F.Multiply(Vector512.LoadUnsafe(ref Unsafe.Add(ref ptr, 48)), scale);

            // Horner's method for exp(x) ≈ 1 + x + x²/2 + x³/6 + ...
            // exp(v0)
            var r0 = Avx512F.FusedMultiplyAdd(c5, v0, c4);
            r0 = Avx512F.FusedMultiplyAdd(r0, v0, c3);
            r0 = Avx512F.FusedMultiplyAdd(r0, v0, c2);
            r0 = Avx512F.FusedMultiplyAdd(r0, v0, c1);
            r0 = Avx512F.FusedMultiplyAdd(r0, v0, c0);

            // exp(v1)
            var r1 = Avx512F.FusedMultiplyAdd(c5, v1, c4);
            r1 = Avx512F.FusedMultiplyAdd(r1, v1, c3);
            r1 = Avx512F.FusedMultiplyAdd(r1, v1, c2);
            r1 = Avx512F.FusedMultiplyAdd(r1, v1, c1);
            r1 = Avx512F.FusedMultiplyAdd(r1, v1, c0);

            // exp(v2)
            var r2 = Avx512F.FusedMultiplyAdd(c5, v2, c4);
            r2 = Avx512F.FusedMultiplyAdd(r2, v2, c3);
            r2 = Avx512F.FusedMultiplyAdd(r2, v2, c2);
            r2 = Avx512F.FusedMultiplyAdd(r2, v2, c1);
            r2 = Avx512F.FusedMultiplyAdd(r2, v2, c0);

            // exp(v3)
            var r3 = Avx512F.FusedMultiplyAdd(c5, v3, c4);
            r3 = Avx512F.FusedMultiplyAdd(r3, v3, c3);
            r3 = Avx512F.FusedMultiplyAdd(r3, v3, c2);
            r3 = Avx512F.FusedMultiplyAdd(r3, v3, c1);
            r3 = Avx512F.FusedMultiplyAdd(r3, v3, c0);

            // Store results
            r0.StoreUnsafe(ref ptr);
            r1.StoreUnsafe(ref Unsafe.Add(ref ptr, 16));
            r2.StoreUnsafe(ref Unsafe.Add(ref ptr, 32));
            r3.StoreUnsafe(ref Unsafe.Add(ref ptr, 48));
        }
    }

    public void Dispose()
    {
        foreach (var buffer in _threadBuffers.Values)
            buffer.Dispose();
        _threadBuffers.Dispose();
    }
}