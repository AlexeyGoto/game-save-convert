using System.Net.Http;

namespace SaveConvert;

/// <summary>
/// Steam ID helpers + optional remote list download for fast list search.
/// v4: no server dependency. Downloads steam_ids.txt as accelerator, never blocks.
/// </summary>
static class SteamIds
{
    const long Steam64Base = 76561197960265728L;
    const string Url = "https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt";

    public static async Task<List<string>> TryDownloadAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            string url = $"{Url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            string raw = await client.GetStringAsync(url);
            return Parse(raw);
        }
        catch { return new List<string>(); }
    }

    public static List<string> Parse(string raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        foreach (string line in raw.Split('\n'))
        {
            string l = line.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;

            // Remove spaces inside numbers ("1 915 550 405" → "1915550405")
            string cleaned = l.Replace(" ", "");

            if (!IsDigits(cleaned)) continue;

            string id64 = ToSteam64(cleaned);
            if (seen.Add(id64))
                result.Add(id64);
        }

        return result;
    }

    public static string ToSteam64(string id)
    {
        if (!long.TryParse(id, out long val)) return id;
        return val < Steam64Base ? (val + Steam64Base).ToString() : id;
    }

    public static string ToSteam32(string id)
    {
        if (!long.TryParse(id, out long val)) return id;
        return val >= Steam64Base ? (val - Steam64Base).ToString() : id;
    }

    public static bool IsDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s)
            if (c < '0' || c > '9') return false;
        return true;
    }

}
