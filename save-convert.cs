using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

[assembly: AssemblyTitle("Game Save Convert")]
[assembly: AssemblyDescription("Save file converter for RE Engine games")]
[assembly: AssemblyCompany("AlexeyGoto")]
[assembly: AssemblyProduct("Game Save Convert")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyVersion("1.1.0.0")]

class SaveConvert
{
    static string installDir = @"C:\Tools\SaveCompat";
    static string cli;
    static string profile;
    static string logFile;
    static string lastStdout;
    static string lastStderr;
    static string idsUrl = "https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt";
    static long STEAM64_BASE = 76561197960265728L;

    static void Log(string msg)
    {
        try
        {
            File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
        }
        catch { }
    }

    static void Die(string msg, int code)
    {
        Console.Error.WriteLine("ERROR: " + msg);
        Log("ERROR: " + msg);
        Environment.Exit(code);
    }

    // Convert Steam32 to Steam64 or vice versa
    static string ToSteam64(string id)
    {
        long val;
        if (!long.TryParse(id, out val)) return id;
        if (val < STEAM64_BASE)
            return (val + STEAM64_BASE).ToString();
        return id; // already Steam64
    }

    static string ToSteam32(string id)
    {
        long val;
        if (!long.TryParse(id, out val)) return id;
        if (val >= STEAM64_BASE)
            return (val - STEAM64_BASE).ToString();
        return id; // already Steam32
    }

    static bool IsSteam64(string id)
    {
        long val;
        if (!long.TryParse(id, out val)) return false;
        return val >= STEAM64_BASE;
    }

    static int Main(string[] args)
    {
        logFile = Path.Combine(installDir, "save-convert.log");
        cli = Path.Combine(installDir, @"mandarin\mandarin-juice-cli.exe");

        File.WriteAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===== START =====" + Environment.NewLine);

        // ===== Parse arguments =====
        string targetId = null;
        string savePath = null;

        foreach (string arg in args)
        {
            string a = arg;
            if (a.StartsWith("-"))
                a = a.Substring(1);
            if (targetId == null && IsDigits(a))
                targetId = a;
            else if (savePath == null)
                savePath = a;
        }

        if (targetId == null || savePath == null)
        {
            string msg = "Usage: save-convert.exe -<target_steam_id> -<save_folder_path>\n"
                + "Example: save-convert.exe -22202 -\"C:\\path\\to\\saves\"\n"
                + "Steam ID can be Steam32 (short) or Steam64 (17 digits). Auto-converted.";
            Console.Error.WriteLine(msg);
            Log(msg);
            return 1;
        }

        // MandarinJuice uses Steam64 format — auto-convert
        string targetId64 = ToSteam64(targetId);
        string targetId32 = ToSteam32(targetId);
        Log("Target ID (input): " + targetId);
        Log("Target ID (Steam64): " + targetId64);
        Log("Target ID (Steam32): " + targetId32);
        Log("Save path: " + savePath);

        // ===== Validate paths =====
        if (!File.Exists(cli))
            Die("MandarinJuice not found at " + cli + ". Run install.ps1 first.", 1);

        // Find all profiles in _profiles
        string profDir = Path.Combine(installDir, @"mandarin\_profiles");
        if (!Directory.Exists(profDir))
            Die("Profiles directory not found: " + profDir, 1);

        List<string> profiles = new List<string>();
        foreach (string f in Directory.GetFiles(profDir, "*.bin"))
        {
            profiles.Add(f);
        }
        if (profiles.Count == 0)
            Die("No profile .bin found in " + profDir, 1);
        Log("Found " + profiles.Count + " profiles:");
        foreach (string p in profiles)
            Log("  " + Path.GetFileName(p));

        // ===== Verify MandarinJuice can run =====
        Log("Verifying MandarinJuice...");
        int verifyEc = RunMandarin("-h");
        if (lastStderr != null && lastStderr.Contains(".NET"))
        {
            Die("MandarinJuice requires .NET runtime. Install .NET 10:\n"
                + "  powershell -Command \"irm https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/install.ps1 | iex\"", 1);
        }
        Log("MandarinJuice OK (exit code " + verifyEc + ")");

        // ===== Check save folder =====
        if (!Directory.Exists(savePath))
        {
            Log("Save folder does not exist, new game. OK.");
            return 0;
        }

        // Get .bin files (excluding backup_* subfolders)
        List<string> saveFiles = new List<string>();
        foreach (string f in Directory.GetFiles(savePath, "*.bin"))
        {
            saveFiles.Add(f);
        }

        if (saveFiles.Count == 0)
        {
            Log("No .bin files in save folder, new game. OK.");
            return 0;
        }
        Log("Found " + saveFiles.Count + " save files");

        // ===== Download steam_ids.txt =====
        string workDir = Path.Combine(Path.GetTempPath(), "save-compat-work");
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        string idsFile = Path.Combine(workDir, "steam_ids.txt");
        Log("Downloading steam_ids.txt...");
        try
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile(idsUrl, idsFile);
            }
        }
        catch (Exception ex)
        {
            Die("Failed to download steam_ids.txt: " + ex.Message, 1);
        }

        if (!File.Exists(idsFile))
            Die("steam_ids.txt download failed", 1);

        // ===== Read IDs (convert all to Steam64 for MandarinJuice, deduplicate) =====
        List<string> ids64 = new List<string>();
        HashSet<string> seen = new HashSet<string>();
        foreach (string line in File.ReadAllLines(idsFile))
        {
            string l = line.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            if (IsDigits(l))
            {
                string id64 = ToSteam64(l);
                if (!seen.Contains(id64))
                {
                    seen.Add(id64);
                    ids64.Add(id64);
                }
            }
        }
        Log("Loaded " + ids64.Count + " unique Steam64 IDs from list");

        // ===== Try target ID first (saves already compatible?) =====
        string testDir = Path.Combine(workDir, "test");
        Directory.CreateDirectory(testDir);

        string testFile = saveFiles[0]; // first .bin
        File.Copy(testFile, Path.Combine(testDir, Path.GetFileName(testFile)), true);

        Log("Testing compatibility with target ID " + targetId64 + "...");
        foreach (string prof in profiles)
        {
            profile = prof;
            ResetTestDir(testDir, testFile);

            if (TryDecrypt(testDir, targetId64))
            {
                Log("Saves already compatible with target ID (profile: " + Path.GetFileName(prof) + "). OK.");
                Console.WriteLine("OK: Saves already compatible with target Steam ID.");
                Cleanup(workDir);
                return 0;
            }
        }

        // ===== Brute-force: try all IDs x all profiles =====
        Log("Saves not compatible. Brute-forcing " + ids64.Count + " IDs x " + profiles.Count + " profiles...");
        string sourceId64 = null;
        string foundProfile = null;
        int attempt = 0;
        int total = ids64.Count * profiles.Count;

        for (int i = 0; i < ids64.Count; i++)
        {
            string id = ids64[i];
            if (id == targetId64) continue;

            foreach (string prof in profiles)
            {
                profile = prof;
                attempt++;

                ResetTestDir(testDir, testFile);

                if (TryDecrypt(testDir, id))
                {
                    sourceId64 = id;
                    foundProfile = prof;
                    Log("FOUND source ID: " + sourceId64 + " (Steam32: " + ToSteam32(sourceId64) + ") with profile: " + Path.GetFileName(prof) + " (attempt " + attempt + "/" + total + ")");
                    break;
                }
            }
            if (sourceId64 != null) break;
        }

        if (sourceId64 == null)
        {
            Log("Source ID not found in list");
            Console.Error.WriteLine("ERROR: Could not determine source Steam ID. Add more IDs to steam_ids.txt.");
            Cleanup(workDir);
            return 2;
        }

        // Use the found profile for re-signing
        profile = foundProfile;

        // ===== Create backup =====
        string backupName = string.Format("backup_{0}_{1}_{2}",
            ToSteam32(sourceId64), targetId32, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        string backupDir = Path.Combine(savePath, backupName);
        Directory.CreateDirectory(backupDir);

        Log("Creating backup: " + backupName);
        foreach (string f in saveFiles)
        {
            File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
        }

        // Write info.txt
        StringBuilder info = new StringBuilder();
        info.AppendLine("Source Steam ID: " + sourceId64 + " (Steam32: " + ToSteam32(sourceId64) + ")");
        info.AppendLine("Target Steam ID: " + targetId64 + " (Steam32: " + targetId32 + ")");
        info.AppendLine("Profile: " + Path.GetFileName(foundProfile));
        info.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        info.AppendLine("Files:");
        foreach (string f in saveFiles)
            info.AppendLine("  " + Path.GetFileName(f));
        File.WriteAllText(Path.Combine(backupDir, "info.txt"), info.ToString());

        // ===== Delete old backups (keep 3) =====
        List<string> backups = new List<string>();
        foreach (string d in Directory.GetDirectories(savePath, "backup_*"))
        {
            backups.Add(d);
        }
        backups.Sort();
        while (backups.Count > 3)
        {
            string old = backups[0];
            backups.RemoveAt(0);
            try
            {
                Directory.Delete(old, true);
                Log("Deleted old backup: " + Path.GetFileName(old));
            }
            catch { }
        }

        // ===== Re-sign saves =====
        string resignDir = Path.Combine(workDir, "resign");
        Directory.CreateDirectory(resignDir);

        foreach (string f in saveFiles)
        {
            File.Copy(f, Path.Combine(resignDir, Path.GetFileName(f)), true);
        }

        // Clean up any previous _OUTPUT in mandarin dir
        string mandarinOutputDir = Path.Combine(installDir, @"mandarin\_OUTPUT");
        if (Directory.Exists(mandarinOutputDir))
            try { Directory.Delete(mandarinOutputDir, true); } catch { }

        Log("Re-signing: " + sourceId64 + " -> " + targetId64);
        int ec = RunMandarin("-m r -g \"" + profile + "\" -p \"" + resignDir + "\" -uI " + sourceId64 + " -uO " + targetId64);
        Log("MandarinJuice re-sign exit code: " + ec);

        // MandarinJuice creates _OUTPUT next to its exe:
        // <mandarin_dir>\_OUTPUT\<timestamp>_resigned\<targetId>\*.bin
        bool copied = false;

        if (Directory.Exists(mandarinOutputDir))
        {
            Log("Found _OUTPUT at: " + mandarinOutputDir);
            // Structure: _OUTPUT\<timestamp>_resigned\<targetId>\*.bin
            // Find the newest subfolder matching targetId
            string targetSubDir = null;
            DateTime newest = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(mandarinOutputDir))
            {
                string idDir = Path.Combine(dir, targetId64);
                if (Directory.Exists(idDir))
                {
                    DateTime dt = Directory.GetCreationTime(dir);
                    if (dt > newest)
                    {
                        newest = dt;
                        targetSubDir = idDir;
                    }
                }
            }

            if (targetSubDir != null)
            {
                Log("Using output dir: " + targetSubDir);
                foreach (string f in Directory.GetFiles(targetSubDir, "*.bin"))
                {
                    File.Copy(f, Path.Combine(savePath, Path.GetFileName(f)), true);
                    Log("Copied re-signed: " + Path.GetFileName(f));
                    copied = true;
                }
            }
            else
            {
                // Fallback: grab any .bin recursively (pre-cleaned, so safe)
                Log("Target subfolder not found, searching recursively...");
                foreach (string f in Directory.GetFiles(mandarinOutputDir, "*.bin", SearchOption.AllDirectories))
                {
                    File.Copy(f, Path.Combine(savePath, Path.GetFileName(f)), true);
                    Log("Copied re-signed (recursive): " + Path.GetFileName(f));
                    copied = true;
                }
            }
            // Clean up _OUTPUT
            try { Directory.Delete(mandarinOutputDir, true); } catch { }
        }

        if (copied)
        {
            Console.WriteLine("OK: Saves re-signed from " + ToSteam32(sourceId64) + " to " + targetId32 + ".");
        }
        else
        {
            Log("WARNING: No re-signed files found in _OUTPUT");
            Console.Error.WriteLine("WARNING: Re-sign may have failed. Check save-convert.log.");
        }

        Cleanup(workDir);
        Log("===== DONE =====");
        return 0;
    }

    static void ResetTestDir(string testDir, string testFile)
    {
        foreach (string f in Directory.GetFiles(testDir))
            File.Delete(f);
        // Also clean _OUTPUT if leftover
        string outDir = Path.Combine(testDir, "_OUTPUT");
        if (Directory.Exists(outDir))
            try { Directory.Delete(outDir, true); } catch { }
        File.Copy(testFile, Path.Combine(testDir, Path.GetFileName(testFile)), true);
    }

    static bool TryDecrypt(string dir, string steamId)
    {
        // Clean any previous _OUTPUT
        string mandarinOutput = Path.Combine(installDir, @"mandarin\_OUTPUT");
        if (Directory.Exists(mandarinOutput))
            try { Directory.Delete(mandarinOutput, true); } catch { }

        int ec = RunMandarin("-m d -g \"" + profile + "\" -p \"" + dir + "\" -u " + steamId);

        // Method 1: Check stdout for successful decryption message
        if (lastStdout != null && lastStdout.Contains("Decrypted the"))
        {
            // Clean up _OUTPUT wherever it was created
            if (Directory.Exists(mandarinOutput))
                try { Directory.Delete(mandarinOutput, true); } catch { }
            return true;
        }

        // Method 2: Check if _OUTPUT was created (next to mandarin exe)
        if (Directory.Exists(mandarinOutput))
        {
            bool hasFiles = false;
            foreach (string f in Directory.GetFiles(mandarinOutput, "*", SearchOption.AllDirectories))
            {
                hasFiles = true;
                break;
            }
            try { Directory.Delete(mandarinOutput, true); } catch { }
            return hasFiles;
        }

        return false;
    }

    static int RunMandarin(string arguments)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            if (!string.IsNullOrEmpty(stdout)) Log("  stdout: " + stdout.Trim());
            if (!string.IsNullOrEmpty(stderr)) Log("  stderr: " + stderr.Trim());
            lastStdout = stdout;
            lastStderr = stderr;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log("  RunMandarin exception: " + ex.Message);
            lastStdout = null;
            lastStderr = null;
            return -1;
        }
    }

    static bool IsDigits(string s)
    {
        foreach (char c in s)
            if (c < '0' || c > '9') return false;
        return s.Length > 0;
    }

    static void Cleanup(string workDir)
    {
        try { Directory.Delete(workDir, true); } catch { }
    }
}
