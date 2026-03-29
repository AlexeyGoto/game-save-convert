# Game Save Convert v4.3

[Русская версия](README.md)

A utility for converting Resident Evil 9 Requiem save files between Steam and crack (Goldberg/GSE) versions. Automatically detects, decrypts and re-encrypts saves for your current Steam ID.

## Features

- **Steam <-> Crack** — transfer saves in both directions
- **Auto-detect platform** — based on save folder path (Steam/GSE)
- **Automatic BUILD downgrade** — if save is newer than target version, BUILD is automatically lowered
- **Fast brute-force** — source ID detection in 5-17 seconds (vs ~4 min in v3)
- **Offline-first** — works without internet, ID list used only as accelerator
- **remotecache.vdf** — auto-generated for Steam (with write protection)
- **data00-1.bin handling** — version counter patch to preserve game settings
- **No restrictions** — no approval, whitelist or data transmission required

## What's new in v4.3

- **Automatic downgrade**: BUILD is automatically lowered to `0x01001002` (latest crack-supported version). The `-downgrade` flag has been removed.
- **`-targetsavebuild <build>`**: optional override for the target build (if you need a different version)
- **Downgrade only**: if a file's BUILD <= target — nothing changes. Upgrade is never performed
- **New BUILD**: added support for `0x01002000` (March 2026 Steam update)

## BUILD to game version mapping

| BUILD | Version | Alias |
|-------|---------|-------|
| `0x01001000` | v1.0 (initial release) | `v4`, `crack` |
| `0x01001001` | v1.0.1 | — |
| `0x01001002` | v1.1 (April 2026 patch) | `v5`, `steam` |
| `0x01002000` | v2.0 (March 2026 Steam update) | `v6` |

## How it works

RE9 encrypts save files bound to your Steam ID. When transferring from a different source, saves become unreadable.

The utility automatically:
1. Checks if saves are compatible with your account
2. If not — tries to download the list of known IDs (1 sec)
3. If downloaded — instant list search
4. If unavailable or not found — full brute-force (~5-17 sec, runs once)
5. Re-encrypts saves in a temporary folder
6. Automatically downgrades BUILD if file is newer than target; patches version counter
7. Creates a backup of original files
8. Copies re-encrypted files to the save folder
9. For Steam target — generates `remotecache.vdf` (read-only)

Original files are never modified directly — all work happens in a temporary folder.

## Installation

1. Download `installer.exe` from the [Releases](../../releases/latest) page
2. Run as administrator
3. Click "Install"

The installer automatically:
- Downloads the RE9 encryption profile
- Installs .NET 10 Desktop Runtime (if not installed)
- Downloads and extracts `save-convert.exe` with dependencies
- Adds the utility to system PATH
- Downloads README.md locally

**After installation, you MUST restart your computer** for the system PATH to update. Without a restart, `save-convert.exe` won't be found in the command line.

## !! IMPORTANT: disable Steam Cloud before conversion

**When transferring saves to Steam, you MUST disable cloud sync for RE9:**

1. Open Steam -> Library -> Right-click Resident Evil 9 -> Properties
2. General tab -> uncheck **"Keep game saves in the Steam Cloud"**
3. Run the conversion
4. Launch the game, verify saves loaded correctly
5. You can re-enable sync afterwards

**Without this, Steam will overwrite converted files with the cloud copy and saves won't load!**

---

## Usage

### Save conversion

```
save-convert.exe -<steam_id> -<save_path> -re9 [-silent] [-crack|-steam] [-targetsavebuild <build>]
```

### Benchmark

```
save-convert.exe benchmark
```

### Parameters

| Parameter | Description |
|-----------|-------------|
| `steam_id` | Your Steam ID (numeric) |
| `path` | Full path to the save folder containing `.bin` files |
| `-re9` | Game code (Resident Evil 9 Requiem) |
| `-silent` | No windows or dialogs (for automation) |
| `-crack` | Force target platform — crack |
| `-steam` | Force target platform — Steam |
| `-targetsavebuild <build>` | Override target BUILD for downgrade. Accepts hex (`0x01001000`) or alias (`v4`, `v5`, `v6`, `crack`, `steam`). Default: `0x01001002` |

All parameters except `-silent`, `-crack`, `-steam`, `-targetsavebuild` are required. Order doesn't matter, but each must start with `-`.

### Examples

**Crack -> Steam**:
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\3764200\remote\win64_save" -re9
```

**Steam -> Crack**:
```
save-convert.exe -76561197960287930 -"C:\Users\User\AppData\Roaming\GSE Saves\3764200\remote\win64_save" -re9
```

**With custom target BUILD**:
```
save-convert.exe -76561197960287930 -"C:\Users\User\AppData\Roaming\GSE Saves\3764200\remote\win64_save" -re9 -targetsavebuild v4
```

Silent mode:
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\3764200\remote\win64_save" -re9 -silent
```

### Auto-launch via Steam

You can set up automatic conversion on every RE9 launch via Steam.

**Steam -> Library -> Right-click RE9 -> Properties -> Launch Options:**

```
cmd /c start /wait "" "C:\Tools\SaveCompat\save-convert.exe" -YOUR_STEAM_ID -"C:\Program Files (x86)\Steam\userdata\YOUR_STEAM32\3764200\remote\win64_save" -re9 -silent & start "" %command%
```

### Where to find your Steam ID

Your Steam ID is a numeric account identifier. You can find it:
- On your Steam profile page (URL contains the ID)
- In emulator settings (Goldberg, GreenLuma, etc.)
- In crack configs (e.g., `ColdClientLoader.ini` -> `AccountId` parameter)

## Automatic BUILD downgrade

Downgrade is performed automatically on every conversion. If a file's BUILD is higher than the target (`0x01001002` by default), it's lowered. If the file's BUILD is equal or lower — nothing changes.

This ensures compatibility when transferring saves from newer Steam versions to older crack versions.

Use `-targetsavebuild <build>` to override the target build.

The downgrade affects:
- All `data000.bin`, `*Slot*.bin` files — BUILD at offset 0x5C
- `data00-1.bin` — BUILD at offset 0x4C + version counter patch (offset 0x28)

The version counter in `data00-1.bin` is incremented by +2 only if BUILD was actually lowered, so the game accepts the settings file.

## Exit codes

| Code | Meaning | Action |
|------|---------|--------|
| 0 | Saves already compatible or successfully converted | Nothing, all OK |
| 1 | Error (cancel, failure, unsupported version) | Check `save-convert.log` |
| 2 | Source Steam ID not found | Full brute-force yielded no results |

## Backups

Before each conversion, a backup of original files is automatically created. The 3 most recent backups are kept, older ones are deleted automatically.

To restore: copy `.bin` files from the backup folder back to the save folder.

## Third-party components

- [MandarinJuice](https://github.com/mi5hmash/MandarinJuice) — RE Engine save encryption/decryption engine ([MIT License](https://github.com/mi5hmash/MandarinJuice/blob/master/LICENSE))

## License

MIT License
