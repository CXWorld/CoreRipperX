#include "pch.h"
#include "AVX2Operations.h"
#include <immintrin.h>
#include <windows.h>

extern "C" __declspec(dllexport) void PerformHeavyLoad()
{
    __m256i vec = _mm256_set1_epi32(1);
    for (int i = 0; i < 1000000000; i++)
    {
        vec = _mm256_add_epi32(vec, _mm256_set1_epi32(1));
    }
}