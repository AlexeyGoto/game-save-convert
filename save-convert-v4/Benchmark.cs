using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SaveConvert;

/// <summary>
/// Standalone brute-force speed benchmark.
/// Measures raw throughput of CheckHeaderPreFilter-equivalent loop.
/// Run: save-convert.exe benchmark
/// </summary>
static class Benchmark
{
    public static void Run()
    {
        Console.WriteLine("=== BruteForce Benchmark (save-convert v4) ===");
        Console.WriteLine($"Logical CPUs: {Environment.ProcessorCount}");
        Console.WriteLine();

        // Simulate pre-filter: same SplitMix64 math as real brute-force
        // 16 header checksum calls + 8 byte comparisons per candidate
        ulong fakeStateAfterQueue = 0xABCDEF0123456789;
        byte[] fakeExpected = new byte[8];
        // Fill with values that will NEVER match (all 0xFF) → every candidate rejected at byte 0
        for (int i = 0; i < 8; i++) fakeExpected[i] = 0xFF;

        // Warmup
        Console.Write("Warmup... ");
        RunChunked(fakeStateAfterQueue, fakeExpected, 10_000_000, Environment.ProcessorCount);
        Console.WriteLine("done");

        // Single-threaded benchmark
        long singleCount = 100_000_000; // 100M
        Console.Write($"Single-thread ({singleCount:N0} IDs)... ");
        var sw = Stopwatch.StartNew();
        RunChunked(fakeStateAfterQueue, fakeExpected, singleCount, 1);
        sw.Stop();
        double singleRate = singleCount / sw.Elapsed.TotalSeconds;
        double singleEta = 4_294_967_296.0 / singleRate;
        Console.WriteLine($"{singleRate:N0} ID/sec  (full scan: {singleEta:N0} sec)");

        // Multi-threaded benchmark
        long multiCount = 500_000_000; // 500M
        int threads = Environment.ProcessorCount;
        Console.Write($"Multi-thread x{threads} ({multiCount:N0} IDs)... ");
        sw.Restart();
        RunChunked(fakeStateAfterQueue, fakeExpected, multiCount, threads);
        sw.Stop();
        double multiRate = multiCount / sw.Elapsed.TotalSeconds;
        double multiEta = 4_294_967_296.0 / multiRate;
        Console.WriteLine($"{multiRate:N0} ID/sec  (full scan: {multiEta:N0} sec)");

        Console.WriteLine();
        Console.WriteLine($"Speedup: {multiRate / singleRate:F1}x");
        Console.WriteLine($"Estimated full brute-force: ~{multiEta:F0} seconds ({multiEta / 60:F1} min)");
    }

    static void RunChunked(ulong stateAfterQueue, byte[] expected, long totalIds, int maxThreads)
    {
        const int chunkSize = 65_536;
        long totalChunks = (totalIds + chunkSize - 1) / chunkSize;

        Parallel.For(0L, totalChunks,
            new ParallelOptions { MaxDegreeOfParallelism = maxThreads },
            chunkIdx =>
            {
                long start = chunkIdx * chunkSize;
                long end = Math.Min(start + chunkSize, totalIds);

                for (long i = start; i < end; i++)
                {
                    ulong state = stateAfterQueue + (ulong)i;

                    // 16 SplitMix64 calls (CalculateHeaderChecksum) — same as real code
                    SM64(ref state); SM64(ref state); SM64(ref state); SM64(ref state);
                    SM64(ref state); SM64(ref state); SM64(ref state); SM64(ref state);
                    SM64(ref state); SM64(ref state); SM64(ref state); SM64(ref state);
                    SM64(ref state); SM64(ref state); SM64(ref state); SM64(ref state);

                    // DeencryptSliceHeader — first byte check (kills 99.6%)
                    SM64(ref state);
                    if ((byte)state == expected[0])
                    {
                        // Would continue checking bytes 1-7 here
                        // In benchmark this never triggers (expected = 0xFF)
                    }
                }
            });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SM64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;
        state = (state ^ (state >> 0x1E)) * 0xBF58476D1CE4E5B9;
        state = (state ^ (state >> 0x1B)) * 0x94D049BB133111EB;
        state ^= state >> 0x1F;
    }
}
