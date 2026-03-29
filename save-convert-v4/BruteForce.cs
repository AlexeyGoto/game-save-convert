using System.Reflection;
using System.Runtime.CompilerServices;
using MandarinJuiceCore.GameProfile;
using MandarinJuiceCore.Helpers;
using MandarinJuiceCore.Models.DSSS.Mandarin;

namespace SaveConvert;

/// <summary>
/// Fast Steam ID search using HeaderKey pre-filter + full verification.
/// v4: chunk-based parallelism (no per-iteration Interlocked), thread-local buffers.
/// </summary>
static class BruteForce
{
    const long Steam64Base = 76561197960265728L;

    /// <summary>
    /// Checks a list of candidate Steam64 IDs using HeaderKey pre-filter.
    /// Returns the matching Steam64 string, or null if none match.
    /// </summary>
    public static string? TryFromList(
        IReadOnlyList<string> ids64,
        string skipId64,
        byte[] saveFileData,
        MandarinGameProfile profile)
    {
        var ctx = PrepareContext(saveFileData, profile);
        if (ctx == null) return null;

        foreach (string id64str in ids64)
        {
            if (id64str == skipId64) continue;
            if (!long.TryParse(id64str, out long steam64)) continue;

            long steam32 = steam64 - Steam64Base;
            ulong userId = ApplyParseVariant(steam64, steam32, profile.ParseVariant);

            if (CheckHeaderPreFilter(ctx, userId) && VerifyFull(ctx, userId))
                return steam64.ToString();
        }

        return null;
    }

    /// <summary>
    /// Full brute-force search over Steam32 range [0..4,294,967,295].
    /// v4: chunk-based — Interlocked only per chunk, not per ID.
    /// Reports progress via callback. Supports cancellation.
    /// Returns matching Steam64 string, or null.
    /// </summary>
    public static string? RunFull(
        byte[] saveFileData,
        MandarinGameProfile profile,
        Action<long, long, double>? onProgress,
        CancellationToken ct)
    {
        var ctx = PrepareContext(saveFileData, profile);
        if (ctx == null) return null;

        const long totalRange = 4_294_967_296L;
        const int chunkSize = 65_536; // 64K IDs per chunk — balance between granularity and overhead
        long totalChunks = (totalRange + chunkSize - 1) / chunkSize;
        long found = -1;
        long chunksCompleted = 0;
        int threadCount = Environment.ProcessorCount;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        Parallel.For(0L, totalChunks,
            new ParallelOptions { MaxDegreeOfParallelism = threadCount, CancellationToken = ct },
            (chunkIdx, loopState) =>
            {
                // Early exit: another thread already found the answer
                if (Interlocked.Read(ref found) >= 0) { loopState.Stop(); return; }

                long start = chunkIdx * chunkSize;
                long end = Math.Min(start + chunkSize, totalRange);

                // Hot loop — no Interlocked, no allocations, no progress inside
                for (long i = start; i < end; i++)
                {
                    long steam64 = i + Steam64Base;
                    ulong userId = ApplyParseVariant(steam64, i, profile.ParseVariant);

                    if (CheckHeaderPreFilter(ctx, userId))
                    {
                        if (VerifyFull(ctx, userId))
                        {
                            Interlocked.Exchange(ref found, steam64);
                            loopState.Stop();
                            return;
                        }
                    }
                }

                // One Interlocked per 64K IDs instead of per ID
                long completed = Interlocked.Increment(ref chunksCompleted);

                // Report progress every ~8M IDs (128 chunks * 64K)
                if (onProgress != null && (completed & 127) == 0)
                {
                    long checkedApprox = Math.Min(completed * chunkSize, totalRange);
                    double elapsed = sw.Elapsed.TotalSeconds;
                    double rate = elapsed > 0 ? checkedApprox / elapsed : 0;
                    onProgress(checkedApprox, totalRange, rate);
                }
            });

        return found >= 0 ? found.ToString() : null;
    }

    #region Internal helpers

    sealed class BruteContext
    {
        public ulong Seed;
        public ulong StateAfterQueue;
        public byte[] ExpectedXorBytes = new byte[64];
        public byte[] EncryptedDataTemplate = [];
        public long DecryptedDataLength;
    }

    static BruteContext? PrepareContext(byte[] saveFileData, MandarinGameProfile profile)
    {
        ulong seed = profile.MandarinSeed;

        // Parse the save file
        var mf = new MandarinFile(new MandarinDeencryptor(seed), profile.MandarinFileFlavor);
        mf.SetFileData(saveFileData, encryptedFilesOnly: true);
        if (!mf.IsEncrypted) return null;

        byte[] encData = mf.Data;
        long decLen = mf.Footer.DecryptedDataLength;

        // Get HeaderKey via reflection
        var headerKeyField = typeof(MandarinDeencryptor).GetField("HeaderKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        ulong[] headerKey = (ulong[])headerKeyField!.GetValue(null)!;

        // Prepare header key bytes
        ulong[] expectedHeader = new ulong[8];
        for (int i = 0; i < Math.Min(headerKey.Length, 8); i++)
            expectedHeader[i] = headerKey[i];
        byte[] headerKeyBytes = new byte[64];
        Buffer.BlockCopy(expectedHeader, 0, headerKeyBytes, 0, 64);

        // Precompute stateAfterQueue
        ulong stateAfterQueue = seed;
        uint laps = ((uint)decLen >> 14) + 1;
        for (uint i = 0; i < laps; i++)
            SplitMix64(ref stateAfterQueue);

        // Precompute expected XOR bytes
        byte[] expectedXorBytes = new byte[64];
        for (int i = 0; i < 64; i++)
            expectedXorBytes[i] = (byte)(encData[i] ^ headerKeyBytes[i]);

        return new BruteContext
        {
            Seed = seed,
            StateAfterQueue = stateAfterQueue,
            ExpectedXorBytes = expectedXorBytes,
            EncryptedDataTemplate = (byte[])encData.Clone(),
            DecryptedDataLength = decLen
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool CheckHeaderPreFilter(BruteContext ctx, ulong userId)
    {
        ulong state = ctx.StateAfterQueue + userId;

        // CalculateHeaderChecksum: 16 SplitMix64 calls (unrolled)
        SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state);
        SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state);
        SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state);
        SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state); SplitMix64(ref state);

        byte[] expected = ctx.ExpectedXorBytes;

        // Check first byte (eliminates 99.6%)
        SplitMix64(ref state);
        if ((byte)state != expected[0]) return false;

        // Check bytes 1-7
        SplitMix64(ref state); if ((byte)state != expected[1]) return false;
        SplitMix64(ref state); if ((byte)state != expected[2]) return false;
        SplitMix64(ref state); if ((byte)state != expected[3]) return false;
        SplitMix64(ref state); if ((byte)state != expected[4]) return false;
        SplitMix64(ref state); if ((byte)state != expected[5]) return false;
        SplitMix64(ref state); if ((byte)state != expected[6]) return false;
        SplitMix64(ref state); if ((byte)state != expected[7]) return false;

        // First 8 bytes match! Check all 64 bytes
        for (int b = 8; b < 64; b++)
        {
            SplitMix64(ref state);
            if ((byte)state != expected[b]) return false;
        }

        return true;
    }

    static bool VerifyFull(BruteContext ctx, ulong userId)
    {
        try
        {
            var de = new MandarinDeencryptor(ctx.Seed);
            byte[] encBuf = (byte[])ctx.EncryptedDataTemplate.Clone();
            byte[] decBuf = new byte[(uint)ctx.DecryptedDataLength];
            de.DecryptData(decBuf, encBuf, userId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong ApplyParseVariant(long steam64, long steam32, uint parseVariant)
    {
        return parseVariant switch
        {
            0 => (ulong)steam64,
            1 => ~(ulong)(int)steam32 | 0xFFFFFFFF00000000,
            2 => ~(ulong)steam64,
            3 => ~GetObfuscatedSteamId64((ulong)steam64),
            _ => (ulong)steam64
        };
    }

    static ulong GetObfuscatedSteamId64(ulong steamId64)
    {
        ulong notSteamId = steamId64 ^ 0x1A3B5C7DD0C2B4A8;
        return ((notSteamId >> 32) & 0xFF) |
               (((notSteamId >> 40) & 0xFF) << 8) |
               (((notSteamId >> 48) & 0xFF) << 16) |
               (((notSteamId >> 56) & 0xFF) << 24) |
               ((notSteamId & 0xFF) << 32) |
               (((notSteamId >> 8) & 0xFF) << 40) |
               (((notSteamId >> 16) & 0xFF) << 48) |
               (((notSteamId >> 24) & 0xFF) << 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;
        state = (state ^ (state >> 0x1E)) * 0xBF58476D1CE4E5B9;
        state = (state ^ (state >> 0x1B)) * 0x94D049BB133111EB;
        state ^= state >> 0x1F;
    }

    #endregion
}
