using System.Runtime.InteropServices;
using System.Text;
using MandarinJuiceCore.GameProfile;
using MandarinJuiceCore.Helpers;
using MandarinJuiceCore.Infrastructure;
using MandarinJuiceCore.Models.DSSS.Mandarin;
using Mi5hmasH.GameProfile;
using SaveConvert;

// ============================================================
// Game Save Convert v4.3 — fully local, no server dependencies
// v4.3: auto-downgrade to DefaultTargetBuild, -targetsavebuild override
// ============================================================

string InstallDir = Path.GetDirectoryName(Environment.ProcessPath) ?? @"C:\Tools\SaveCompat";
const long Steam64Base = 76561197960265728L;

string logFile = Path.Combine(InstallDir, "save-convert.log");
string workDir = Path.Combine(Path.GetTempPath(), "save-compat-work");

// No local cache — steam_ids.txt downloaded as accelerator, brute-force as fallback

// ===== Logging =====
void Log(string msg)
{
    try { File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); }
    catch { }
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

bool silentMode = false;

void ShowError(string msg)
{
    if (!silentMode)
        MessageBox(IntPtr.Zero, msg, "Game Save Convert", 0x10);
}

void Cleanup()
{
    try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
}

int Die(string msg, int code)
{
    Log("ERROR: " + msg);
    Cleanup();
    return code;
}

try { File.WriteAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ===== START v4.3 ====={Environment.NewLine}"); }
catch { }

// Clean leftover temp from previous runs/crashes
Cleanup();

// ===== Benchmark command =====
if (args.Length >= 1 && args[0].Equals("benchmark", StringComparison.OrdinalIgnoreCase))
{
    Benchmark.Run();
    return 0;
}

// ===== Parse arguments =====
string? targetId = null;
string? savePath = null;
string? gameFilter = null;
SavePatching.SaveTarget? forceTarget = null;
uint targetBuild = SavePatching.DefaultTargetBuild;

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    string a = arg.StartsWith('-') ? arg[1..] : arg;
    if (a.Equals("silent", StringComparison.OrdinalIgnoreCase))
    {
        silentMode = true;
        continue;
    }
    if (a.Equals("crack", StringComparison.OrdinalIgnoreCase))
    {
        forceTarget = SavePatching.SaveTarget.Crack;
        continue;
    }
    if (a.Equals("steam", StringComparison.OrdinalIgnoreCase))
    {
        forceTarget = SavePatching.SaveTarget.Steam;
        continue;
    }
    if (a.Equals("targetsavebuild", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length)
            return Die("-targetsavebuild requires a value (e.g. -targetsavebuild 0x01001000 or -targetsavebuild v4)", 1);
        string buildArg = args[++i];
        if (!SavePatching.TryParseBuild(buildArg, out uint parsedBuild))
            return Die($"Invalid build value '{buildArg}'. Use hex (0x01001000) or alias (v4, v5, v6, crack, steam)", 1);
        targetBuild = parsedBuild;
        continue;
    }
    if (targetId == null && SteamIds.IsDigits(a))
        targetId = a;
    else if (savePath == null && (a.Contains('\\') || a.Contains('/') || a.Contains(':')))
        savePath = a;
    else if (gameFilter == null && a.Length > 0)
        gameFilter = a;
}

if (targetId == null || savePath == null || gameFilter == null)
    return Die("Missing arguments. Usage: save-convert.exe -<steam_id> -<save_path> -<game> [-silent] [-crack|-steam] [-targetsavebuild <build>]", 1);

Log($"Silent mode: {silentMode}");

string targetId64 = SteamIds.ToSteam64(targetId);
string targetId32 = SteamIds.ToSteam32(targetId);
Log($"Target: {targetId} -> Steam64={targetId64}, Steam32={targetId32}");
Log($"Save path: {savePath}");

// ===== Detect save target =====
var saveTarget = forceTarget ?? SavePatching.DetectTarget(savePath);
Log($"Target platform: {saveTarget}" + (forceTarget != null ? " (forced)" : " (auto-detected)"));

// ===== Resolve game alias =====
string? gameName = ResolveGameAlias(gameFilter);
if (gameName == null)
    return Die($"Unknown game: '{gameFilter}'. Valid: re9", 1);
Log($"Game: {gameFilter} -> {gameName}");

// ===== Load profiles =====
string profDir = Path.Combine(InstallDir, @"mandarin\_profiles");
if (!Directory.Exists(profDir))
    return Die("Profiles directory not found: " + profDir, 1);

var profileEntries = new List<(string path, MandarinGameProfile profile)>();
foreach (string f in Directory.GetFiles(profDir, "*.bin"))
{
    if (Path.GetFileName(f).IndexOf(gameName, StringComparison.OrdinalIgnoreCase) < 0)
        continue;
    try
    {
        var gpManager = new GameProfileManager<MandarinGameProfile>();
        gpManager.SetEncryptor(Keychain.GpMagic);
        gpManager.Load(f);
        profileEntries.Add((f, gpManager.GameProfile));
    }
    catch (Exception ex)
    {
        Log($"  Failed to load profile {Path.GetFileName(f)}: {ex.Message}");
    }
}

if (profileEntries.Count == 0)
    return Die($"No profile for '{gameFilter}'. Reinstall to get profiles.", 1);

Log($"Loaded {profileEntries.Count} profile(s):");
foreach (var (path, _) in profileEntries)
    Log($"  {Path.GetFileName(path)}");

// ===== Check save folder =====
if (!Directory.Exists(savePath))
{
    Log("Save folder does not exist, new game. OK.");
    return 0;
}

var allBinFiles = Directory.GetFiles(savePath, "*.bin")
    .Where(f => !f.Contains("backup_", StringComparison.OrdinalIgnoreCase))
    .ToList();

// Separate data00-1.bin for special handling
string? data001File = allBinFiles.FirstOrDefault(f =>
    Path.GetFileName(f).Equals("data00-1.bin", StringComparison.OrdinalIgnoreCase));
var saveFiles = allBinFiles
    .Where(f => !Path.GetFileName(f).Equals("data00-1.bin", StringComparison.OrdinalIgnoreCase))
    .ToList();

if (saveFiles.Count == 0)
{
    Log("No .bin files in save folder, new game. OK.");
    return 0;
}
Log($"Found {saveFiles.Count} save files" + (data001File != null ? " + data00-1.bin" : ""));

// ===== Pick test file =====
string testFile = PickTestFile(saveFiles);
byte[] testData = File.ReadAllBytes(testFile);
Log($"Test file: {Path.GetFileName(testFile)} ({testData.Length} bytes)");

// ===== Test: already compatible? =====
Log($"Testing compatibility with target ID {targetId64}...");
foreach (var (path, profile) in profileEntries)
{
    ulong userId64 = ParseUserId(targetId64, profile.ParseVariant);
    if (SaveOperations.TryDecrypt(testData, profile, userId64))
    {
        Log($"Saves already compatible (profile: {Path.GetFileName(path)}). OK.");
        // Even if compatible, generate remotecache.vdf for Steam target
        if (saveTarget == SavePatching.SaveTarget.Steam)
        {
            try
            {
                RemoteCacheGenerator.Generate(savePath);
                Log("remotecache.vdf generated (saves already compatible)");
            }
            catch (Exception ex) { Log($"remotecache.vdf failed: {ex.Message}"); }
        }
        return 0;
    }
}

// ===== Try download steam_ids.txt (accelerator, not blocker) =====
string? sourceId64 = null;
string? foundProfilePath = null;
MandarinGameProfile? foundProfile = null;

Log("Downloading steam_ids.txt...");
List<string> ids64 = await SteamIds.TryDownloadAsync();
if (ids64.Count > 0)
{
    Log($"Downloaded {ids64.Count} known IDs — list search");

    foreach (var (path, profile) in profileEntries)
    {
        string? result = BruteForce.TryFromList(ids64, targetId64, testData, profile);
        if (result != null)
        {
            sourceId64 = result;
            foundProfilePath = path;
            foundProfile = profile;
            Log($"FOUND via list: {sourceId64} (Steam32: {SteamIds.ToSteam32(sourceId64)}) profile: {Path.GetFileName(path)}");
            break;
        }
    }
    if (sourceId64 == null)
        Log("  Not found in list, will try brute-force");
}
else
{
    Log("Offline mode — steam_ids.txt unavailable, will use brute-force");
}

// ===== Full brute-force if list failed =====
bool cancelled = false;
if (sourceId64 == null)
{
    Log("Starting full brute-force...");

    foreach (var (path, profile) in profileEntries)
    {
        Log($"Brute-force with profile: {Path.GetFileName(path)}");

        (string? bruteResult, bool wasCancelled) = silentMode
            ? RunBruteForceSilent(testData, profile)
            : RunBruteForceWithUI(testData, profile);

        if (wasCancelled)
        {
            cancelled = true;
            break;
        }

        if (bruteResult != null)
        {
            sourceId64 = bruteResult;
            foundProfilePath = path;
            foundProfile = profile;
            Log($"BRUTE-FORCE FOUND: {sourceId64} (Steam32: {SteamIds.ToSteam32(sourceId64)}) profile: {Path.GetFileName(path)}");
            break;
        }
    }
}

// Cancel pressed — exit with error code so caller can detect and skip game launch
if (cancelled)
{
    Log("Brute-force cancelled by user");
    Cleanup();
    return 1;
}

if (sourceId64 == null || foundProfile == null || foundProfilePath == null)
{
    Log("Source ID not found.");
    ShowError("Сохранения несовместимы.\nНе удалось определить исходный аккаунт сохранений.\n\nОбратитесь к разработчику для добавления поддержки.");
    return Die("Source ID not found", 2);
}

// ===== Version compatibility check =====
Log($"Target build: 0x{targetBuild:X8}" + (SavePatching.GetBuildLabel(targetBuild) is string lbl ? $" ({lbl})" : ""));
{
    var data000File = saveFiles.FirstOrDefault(f =>
        Path.GetFileName(f).Equals("data000.bin", StringComparison.OrdinalIgnoreCase));

    if (data000File != null)
    {
        ulong srcUserId = ParseUserId(sourceId64, foundProfile.ParseVariant);
        var ver = SaveOperations.ReadSaveVersion(File.ReadAllBytes(data000File), foundProfile, srcUserId);
        if (ver != null)
            Log($"Save version: v={ver.Value.version}, build=0x{ver.Value.build:X8}");
    }
}

// ===== Step 1: Re-sign ALL files in temp folder =====
// Originals are NEVER modified directly. All work happens in temp.
Log($"Re-signing in temp: {sourceId64} -> {targetId64}");
ulong sourceUserId = ParseUserId(sourceId64, foundProfile.ParseVariant);
ulong targetUserId = ParseUserId(targetId64, foundProfile.ParseVariant);

string tempResignDir = Path.Combine(workDir, "resign");
if (Directory.Exists(tempResignDir)) Directory.Delete(tempResignDir, true);
Directory.CreateDirectory(tempResignDir);

int resigned = 0;
int buildPatchCount = 0;
foreach (string f in saveFiles)
{
    try
    {
        byte[] fileData = File.ReadAllBytes(f);
        var (newData, skipped, buildPatched) = SaveOperations.ResignFileWithPatch(
            fileData, foundProfile, sourceUserId, targetUserId,
            isData001: false, targetBuild: (uint?)targetBuild);
        File.WriteAllBytes(Path.Combine(tempResignDir, Path.GetFileName(f)), newData);
        if (buildPatched) buildPatchCount++;
        if (skipped)
            Log($"  Skipped (already target): {Path.GetFileName(f)}");
        else
            Log($"  Re-signed: {Path.GetFileName(f)}" + (buildPatched ? " [BUILD patched]" : ""));
        resigned++;
    }
    catch (Exception ex)
    {
        Log($"  FAILED to re-sign {Path.GetFileName(f)}: {ex.Message}");
        ShowError("Ошибка конвертации.\nОригинальные файлы не были изменены.");
        return Die("Re-sign failed on " + Path.GetFileName(f), 1);
    }
}

// ===== Step 1b: data00-1.bin — separate processing =====
if (data001File != null)
{
    try
    {
        byte[] d001Data = File.ReadAllBytes(data001File);
        var (newData, oldVer, newVer, bPatched) = SaveOperations.ProcessData001(
            d001Data, foundProfile, sourceUserId, targetUserId,
            targetBuild: (uint?)targetBuild,
            patchVersion: true);

        File.WriteAllBytes(Path.Combine(tempResignDir, "data00-1.bin"), newData);
        string info = bPatched
            ? $"re-signed + downgrade, ver {oldVer}->{newVer}, build patched"
            : "re-signed";
        Log($"  data00-1.bin: {info}");
    }
    catch (Exception ex)
    {
        Log($"  data00-1.bin: SKIP ({ex.Message}) — game will recreate");
    }
}

if (buildPatchCount > 0)
    Log($"BUILD patched in {buildPatchCount} file(s)");

// ===== Step 2: All re-signed OK — backup originals =====
string backupName = $"backup_{SteamIds.ToSteam32(sourceId64)}_{targetId32}_{DateTime.Now:yyyyMMdd_HHmmss}";
string backupDir = Path.Combine(savePath, backupName);
try
{
    Directory.CreateDirectory(backupDir);
    Log($"Creating backup: {backupName}");

    foreach (string f in saveFiles)
        File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
    if (data001File != null)
        File.Copy(data001File, Path.Combine(backupDir, "data00-1.bin"), true);

    var infoSb = new StringBuilder();
    infoSb.AppendLine($"Source Steam ID: {sourceId64} (Steam32: {SteamIds.ToSteam32(sourceId64)})");
    infoSb.AppendLine($"Target Steam ID: {targetId64} (Steam32: {targetId32})");
    infoSb.AppendLine($"Profile: {Path.GetFileName(foundProfilePath)}");
    infoSb.AppendLine($"Target platform: {saveTarget}");
    infoSb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    if (buildPatchCount > 0)
        infoSb.AppendLine($"BUILD downgrade target: 0x{targetBuild:X8}");
    infoSb.AppendLine("Files:");
    foreach (string f in saveFiles)
        infoSb.AppendLine($"  {Path.GetFileName(f)}");
    if (data001File != null)
        infoSb.AppendLine("  data00-1.bin");
    File.WriteAllText(Path.Combine(backupDir, "info.txt"), infoSb.ToString());
}
catch (Exception ex)
{
    ShowError("Не удалось создать бэкап.\nОригинальные файлы не были изменены.");
    return Die("Backup failed: " + ex.Message, 1);
}

// ===== Step 3: Copy re-signed files from temp to save folder =====
try
{
    foreach (string f in saveFiles)
    {
        string tempFile = Path.Combine(tempResignDir, Path.GetFileName(f));
        if (File.Exists(tempFile))
            File.Copy(tempFile, f, true);
    }
    // Copy re-signed data00-1.bin if exists
    string tempData001 = Path.Combine(tempResignDir, "data00-1.bin");
    if (File.Exists(tempData001) && data001File != null)
        File.Copy(tempData001, data001File, true);

    Log($"OK: {resigned} files re-signed from {SteamIds.ToSteam32(sourceId64)} to {targetId32}");
}
catch (Exception ex)
{
    Log($"CRITICAL: Failed to copy re-signed files: {ex.Message}");
    Log($"Backup at: {backupDir}");
    ShowError($"Ошибка при копировании файлов.\nБэкап сохранён: {backupName}\n\nВосстановите файлы вручную из бэкапа.");
    return Die("Copy re-signed failed: " + ex.Message, 1);
}

// ===== Step 4: remotecache.vdf for Steam target =====
if (saveTarget == SavePatching.SaveTarget.Steam)
{
    try
    {
        RemoteCacheGenerator.Generate(savePath);
        Log("remotecache.vdf generated");
    }
    catch (Exception ex) { Log($"remotecache.vdf failed: {ex.Message}"); }
}

// ===== Step 5: Clean old backups (keep 3) =====
var backups = Directory.GetDirectories(savePath, "backup_*")
    .OrderByDescending(d => d).ToList();
if (backups.Count > 3)
{
    for (int i = 3; i < backups.Count; i++)
    {
        try { Directory.Delete(backups[i], true); Log($"Deleted old backup: {Path.GetFileName(backups[i])}"); }
        catch (Exception ex) { Log($"  Could not delete backup {Path.GetFileName(backups[i])}: {ex.Message}"); }
    }
}

Cleanup();
Log("===== DONE =====");
return 0;


// =====================================================
// Helper functions
// =====================================================

static string? ResolveGameAlias(string filter)
{
    return filter.ToLowerInvariant() switch
    {
        "re9" => "Resident Evil 9",
        _ => null
    };
}

static string PickTestFile(List<string> saveFiles)
{
    var data000 = saveFiles.FirstOrDefault(f =>
        Path.GetFileName(f).Equals("data000.bin", StringComparison.OrdinalIgnoreCase));
    if (data000 != null) return data000;

    var slot = saveFiles.FirstOrDefault(f =>
        Path.GetFileName(f).Contains("Slot", StringComparison.OrdinalIgnoreCase));
    if (slot != null) return slot;

    var safe = saveFiles.FirstOrDefault(f =>
        !Path.GetFileName(f).Equals("data00-1.bin", StringComparison.OrdinalIgnoreCase));
    return safe ?? saveFiles[0];
}

static ulong ParseUserId(string steamId, uint parseVariant)
{
    long val = long.Parse(steamId);
    long steam32 = val >= Steam64Base ? val - Steam64Base : val;
    long steam64 = val >= Steam64Base ? val : val + Steam64Base;

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

static (string? result, bool cancelled) RunBruteForceWithUI(byte[] testData, MandarinGameProfile profile)
{
    string? result = null;
    bool cancelled = false;
    Application.EnableVisualStyles();
    Application.SetHighDpiMode(HighDpiMode.SystemAware);

    var form = new ProgressForm();
    var ct = form.Token;

    var bruteThread = new Thread(() =>
    {
        try
        {
            result = BruteForce.RunFull(testData, profile,
                (checked_, total, rate) => form.UpdateProgress(checked_, total, rate),
                ct);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (result != null)
        {
            form.ShowDone($"Найден ID: {result}");
            Thread.Sleep(1500);
        }

        form.CloseFromThread();
    })
    { IsBackground = true };

    form.Shown += (_, _) => bruteThread.Start();
    Application.Run(form);

    return (result, cancelled);
}

(string? result, bool cancelled) RunBruteForceSilent(byte[] testData, MandarinGameProfile profile)
{
    Log("Brute-force running silently...");
    var cts = new CancellationTokenSource();

    string? result = BruteForce.RunFull(testData, profile,
        (checked_, total, rate) =>
        {
            if (checked_ % 50_000_000 == 0)
                Log($"  Progress: {checked_:N0}/{total:N0} ({rate:N0} ID/sec)");
        },
        cts.Token);

    return (result, false);
}
