namespace SaveConvert;

using System.Security.Cryptography;
using System.Text;

static class RemoteCacheGenerator
{
    public static void Generate(string saveDir, string appId = "3764200")
    {
        // remotecache.vdf: 2 levels up from win64_save -> .../<appId>/remotecache.vdf
        // Path: win64_save -> remote -> <appId> -> remotecache.vdf sits at <appId> level
        string? parent = Path.GetDirectoryName(saveDir);          // remote
        parent = parent != null ? Path.GetDirectoryName(parent) : null; // <appId>
        if (parent == null) return;
        string vdfPath = Path.Combine(parent, "remotecache.vdf");

        var files = Directory.GetFiles(saveDir, "*.bin")
            .Where(f => !Path.GetFileName(f).Contains("backup"))
            .OrderBy(f => f).ToList();

        int changeNumber = files.Count * 2;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sb = new StringBuilder();
        sb.AppendLine($"\"{appId}\"");
        sb.AppendLine("{");
        sb.AppendLine($"\t\"ChangeNumber\"\t\t\"{changeNumber}\"");
        sb.AppendLine($"\t\"OSType\"\t\t\"0\"");

        foreach (var file in files)
        {
            string sha1 = ComputeSha1(file);
            long size = new FileInfo(file).Length;
            string rel = $"win64_save/{Path.GetFileName(file)}";

            sb.AppendLine($"\t\"{rel}\"");
            sb.AppendLine("\t{");
            sb.AppendLine($"\t\t\"root\"\t\t\"0\"");
            sb.AppendLine($"\t\t\"size\"\t\t\"{size}\"");
            sb.AppendLine($"\t\t\"localtime\"\t\t\"{now}\"");
            sb.AppendLine($"\t\t\"time\"\t\t\"{now}\"");
            sb.AppendLine($"\t\t\"remotetime\"\t\t\"{now}\"");
            sb.AppendLine($"\t\t\"sha\"\t\t\"{sha1}\"");
            sb.AppendLine($"\t\t\"syncstate\"\t\t\"4\"");
            sb.AppendLine($"\t\t\"persiststate\"\t\t\"0\"");
            sb.AppendLine($"\t\t\"platformstosync2\"\t\t\"-1\"");
            sb.AppendLine("\t}");
        }
        sb.AppendLine("}");

        // Clear read-only if exists from previous run, so we can overwrite
        if (File.Exists(vdfPath))
            File.SetAttributes(vdfPath, FileAttributes.Normal);

        File.WriteAllText(vdfPath, sb.ToString(), Encoding.ASCII);

        // Set read-only to protect from Steam Cloud overwriting
        File.SetAttributes(vdfPath, FileAttributes.ReadOnly);
    }

    static string ComputeSha1(string filePath)
    {
        using var sha = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(sha.ComputeHash(stream));
    }
}
