namespace SaveConvert;

static class SavePatching
{
    public const string RE9AppId = "3764200";

    public static bool SupportsBuildPatching(string? appId)
        => appId == RE9AppId;

    public const int VersionOffset = 0x28;
    public const int BuildOffsetSlot = 0x5C;      // data000 + slots
    public const int BuildOffsetData001 = 0x4C;    // data00-1.bin

    // Known builds mapped to game versions
    public static readonly (uint build, string label)[] KnownBuildVersions = [
        (0x01001000, "v1.0 (initial release)"),
        (0x01001001, "v1.0.1"),
        (0x01001002, "v1.1 (April 2026 patch)"),
        (0x01002000, "v2.0 (March 2026 Steam update)"),
    ];

    // Default downgrade target = latest known build that crack supports
    public const uint DefaultTargetBuild = 0x01001002;

    static readonly Dictionary<string, uint> BuildAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["v4"]    = 0x01001000,
        ["crack"] = 0x01001000,
        ["v5"]    = 0x01001002,
        ["steam"] = 0x01001002,
        ["v6"]    = 0x01002000,
    };

    static readonly HashSet<uint> KnownBuilds = [0x01001000, 0x01001001, 0x01001002, 0x01002000];

    public static bool IsKnownBuild(uint build) => KnownBuilds.Contains(build);

    public static bool TryParseBuild(string input, out uint build)
    {
        if (BuildAliases.TryGetValue(input, out build))
            return true;

        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out build);

        return uint.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out build);
    }

    public static string? GetBuildLabel(uint build)
    {
        foreach (var (b, label) in KnownBuildVersions)
            if (b == build) return label;
        return null;
    }

    public enum SaveTarget { Steam, Crack, Unknown }

    public static SaveTarget DetectTarget(string savePath)
    {
        string upper = savePath.ToUpperInvariant();
        if (upper.Contains("STEAM")) return SaveTarget.Steam;
        if (upper.Contains("GSE"))   return SaveTarget.Crack;
        return SaveTarget.Unknown;
    }
}
