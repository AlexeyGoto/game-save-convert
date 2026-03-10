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
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyVersion("1.0.0.0")]

class SaveConvert
{
    static string installDir = @"C:\Tools\SaveCompat";
    static string cli;
    static string profile;
    static string logFile;
    static string lastStderr;
    static string idsUrl = "https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt";

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
            // Try as steam ID (all digits, any length — Steam32 can be 1-10 digits)
            if (targetId == null && IsDigits(a))
                targetId = a;
            else if (savePath == null)
                savePath = a;
        }

        if (targetId == null || savePath == null)
        {
            string msg = "Usage: save-convert.exe -<target_steam_id> -<save_folder_path>\n"
                + "Example: save-convert.exe -22202 -\"C:\\path\\to\\saves\"";
            Console.Error.WriteLine(msg);
            Log(msg);
            return 1;
        }

        Log("Target ID: " + targetId);
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
                + "  powershell -Command \"Invoke-WebRequest -Uri 'https://aka.ms/dotnet/10.0/preview/dotnet-runtime-win-x64.exe' -OutFile $env:TEMP\\dotnet10.exe; Start-Process $env:TEMP\\dotnet10.exe -ArgumentList '/install','/quiet','/norestart' -Wait\"", 1);
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

        // ===== Read IDs =====
        List<string> ids = new List<string>();
        foreach (string line in File.ReadAllLines(idsFile))
        {
            string l = line.Trim();
            if (l.Length == 0 || l.StartsWith("#")) continue;
            if (IsDigits(l))
                ids.Add(l);
        }
        Log("Loaded " + ids.Count + " IDs from list");

        if (!ids.Contains(targetId))
        {
            Log("WARNING: target ID " + targetId + " not in list, proceeding anyway");
        }

        // ===== Try target ID first (saves already compatible?) =====
        string testDir = Path.Combine(workDir, "test");
        Directory.CreateDirectory(testDir);

        string testFile = saveFiles[0]; // first .bin
        File.Copy(testFile, Path.Combine(testDir, Path.GetFileName(testFile)), true);

        Log("Testing compatibility with target ID " + targetId + "...");
        // Try all profiles with target ID
        foreach (string prof in profiles)
        {
            profile = prof;
            // Reset test dir
            foreach (string f in Directory.GetFiles(testDir))
                File.Delete(f);
            File.Copy(testFile, Path.Combine(testDir, Path.GetFileName(testFile)), true);

            if (TryDecrypt(testDir, targetId))
            {
                Log("Saves already compatible with target ID (profile: " + Path.GetFileName(prof) + "). OK.");
                Cleanup(workDir);
                return 0;
            }
        }

        // ===== Brute-force: try all IDs x all profiles =====
        Log("Saves not compatible. Brute-forcing " + ids.Count + " IDs x " + profiles.Count + " profiles...");
        string sourceId = null;
        int attempt = 0;
        int total = ids.Count * profiles.Count;

        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            if (id == targetId) continue;

            foreach (string prof in profiles)
            {
                profile = prof;
                attempt++;

                // Reset test dir
                foreach (string f in Directory.GetFiles(testDir))
                    File.Delete(f);
                File.Copy(testFile, Path.Combine(testDir, Path.GetFileName(testFile)), true);

                if (TryDecrypt(testDir, id))
                {
                    sourceId = id;
                    Log("Found source ID: " + sourceId + " with profile: " + Path.GetFileName(prof) + " (attempt " + attempt + "/" + total + ")");
                    break;
                }
            }
            if (sourceId != null) break;
        }

        if (sourceId == null)
        {
            Log("Source ID not found in list");
            Console.Error.WriteLine("ERROR: Could not determine source Steam ID. Add more IDs to steam_ids.txt.");
            Cleanup(workDir);
            return 2;
        }

        // ===== Create backup =====
        string backupName = string.Format("backup_{0}_{1}_{2}",
            sourceId, targetId, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        string backupDir = Path.Combine(savePath, backupName);
        Directory.CreateDirectory(backupDir);

        Log("Creating backup: " + backupName);
        foreach (string f in saveFiles)
        {
            File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
        }

        // Write info.txt
        StringBuilder info = new StringBuilder();
        info.AppendLine("Source Steam ID: " + sourceId);
        info.AppendLine("Target Steam ID: " + targetId);
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

        Log("Re-signing: " + sourceId + " -> " + targetId);
        int ec = RunMandarin("-m r -g \"" + profile + "\" -p \"" + resignDir + "\" -uI " + sourceId + " -uO " + targetId);
        Log("MandarinJuice re-sign exit code: " + ec);

        // Find output files
        string outputDir = Path.Combine(resignDir, "_OUTPUT");
        if (Directory.Exists(outputDir))
        {
            // MandarinJuice creates subfolders in _OUTPUT
            foreach (string subDir in Directory.GetDirectories(outputDir))
            {
                foreach (string f in Directory.GetFiles(subDir, "*.bin"))
                {
                    File.Copy(f, Path.Combine(savePath, Path.GetFileName(f)), true);
                    Log("Copied re-signed: " + Path.GetFileName(f));
                }
            }
            // Also check root of _OUTPUT
            foreach (string f in Directory.GetFiles(outputDir, "*.bin"))
            {
                File.Copy(f, Path.Combine(savePath, Path.GetFileName(f)), true);
                Log("Copied re-signed: " + Path.GetFileName(f));
            }
        }
        else
        {
            Log("WARNING: _OUTPUT directory not found after re-sign");
            Console.Error.WriteLine("WARNING: Re-sign may have failed. Check save-convert.log.");
        }

        Cleanup(workDir);
        Log("===== DONE =====");
        return 0;
    }

    static bool TryDecrypt(string dir, string steamId)
    {
        int ec = RunMandarin("-m d -g \"" + profile + "\" -p \"" + dir + "\" -u " + steamId);
        // Check if _OUTPUT was created with files
        string outDir = Path.Combine(dir, "_OUTPUT");
        if (Directory.Exists(outDir))
        {
            bool hasFiles = false;
            foreach (string f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories))
            {
                hasFiles = true;
                break;
            }
            // Clean up _OUTPUT for next attempt
            try { Directory.Delete(outDir, true); } catch { }
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
            lastStderr = stderr;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log("  RunMandarin exception: " + ex.Message);
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
