# Game Save Convert v5.0

[Русская версия](README.md)

A universal save converter for RE Engine games between Steam and crack (Goldberg/GSE) versions. Automatically detects the game by AppID, decrypts and re-encrypts saves for your current Steam ID.

## Supported Games

| Game | Code | AppID |
|------|------|-------|
| Resident Evil 9 Requiem | `re9`, `requiem` | 3764200 |
| Dragon's Dogma 2 | `dd2`, `dogma2` | 2054970 |
| Monster Hunter Wilds | `mhw`, `wilds` | 2246340 |
| Monster Hunter Stories 3 | `mhs3`, `stories3` | 2852190 |
| Dead Rising Deluxe Remaster | `dr`, `deadrising` | 2527390 |
| Kunitsu-Gami | `kg`, `kunitsu` | 2510710 |
| PRAGMATA | `pragmata` | 3357650 |
| Mega Man Star Force | `mmsf`, `megaman`, `starforce` | 3500390 |

## Features

- **Universal** — supports all RE Engine games with MandarinJuice profiles
- **Auto-detect game** — detection by AppID from save path (`-game` parameter is optional)
- **Steam <-> Crack** — transfer saves in both directions
- **Auto-detect platform** — based on save folder path (Steam/GSE)
- **Automatic BUILD downgrade** — for RE9: if save is newer than target version, BUILD is automatically lowered
- **Fast brute-force** — source ID detection in 5-17 seconds
- **Offline-first** — works without internet, ID list used only as accelerator
- **remotecache.vdf** — auto-generated for Steam (with write protection)
- **No restrictions** — no approval, whitelist or data transmission required

## What's new in v5.0

- **Universal converter**: support for all 8 RE Engine games instead of just RE9
- **Auto-detect game**: AppID is automatically detected from save path — `-game` parameter is now optional
- **Extended aliases**: `re9`, `dd2`, `mhw`, `mhs3`, `dr`, `kg`, `pragmata`, `mmsf`
- **Smart BUILD patching**: BUILD downgrade is only applied for RE9; other games get re-sign only
- **Silent mode graceful**: unknown BUILD in silent mode triggers re-sign without error instead of failure
- **pre-launch-steam.cmd**: supports all 8 games + auto-detection via `%SteamAppId%`
- **Installer**: profiles URL updated to latest — always up-to-date profiles

## How it works

RE Engine games encrypt save files bound to your Steam ID. When transferring from a different source, saves become unreadable.

The utility automatically:
1. Detects the game by AppID from the path (or by `-game` parameter)
2. Checks if saves are compatible with your account
3. If not — tries to download the list of known IDs (1 sec)
4. If downloaded — instant list search
5. If unavailable or not found — full brute-force (~5-17 sec, runs once)
6. Re-encrypts saves in a temporary folder
7. For RE9: automatically downgrades BUILD if file is newer than target
8. Creates a backup of original files
9. Copies re-encrypted files to the save folder
10. For Steam target — generates `remotecache.vdf` (read-only)

Original files are never modified directly — all work happens in a temporary folder.

## Installation

1. Download `installer.exe` from the [Releases](../../releases/latest) page
2. Run as administrator
3. Click "Install"

The installer automatically:
- Downloads encryption profiles for all supported games
- Installs .NET 10 Desktop Runtime (if not installed)
- Downloads and extracts `save-convert.exe` with dependencies
- Adds the utility to system PATH
- Downloads README.md locally

**After installation, you MUST restart your computer** for the system PATH to update. Without a restart, `save-convert.exe` won't be found in the command line.

## !! IMPORTANT: disable Steam Cloud before conversion

**When transferring saves to Steam, you MUST disable cloud sync for the game:**

1. Open Steam -> Library -> Right-click the game -> Properties
2. General tab -> uncheck **"Keep game saves in the Steam Cloud"**
3. Run the conversion
4. Launch the game, verify saves loaded correctly
5. You can re-enable sync afterwards

**Without this, Steam will overwrite converted files with the cloud copy and saves won't load!**

---

## Usage

### Save conversion

```
save-convert.exe -<steam_id> -<save_path> [-game] [-silent] [-crack|-steam] [-targetsavebuild <build>]
```

### Benchmark

```
save-convert.exe benchmark
```

### Parameters

| Parameter | Description |
|-----------|-------------|
| `steam_id` | Your Steam ID (numeric) — required |
| `path` | Full path to the save folder containing `.bin` files — required |
| `-game` | Game code (optional — game is auto-detected from path if omitted) |
| `-silent` | No windows or dialogs (for automation) |
| `-crack` | Force target platform — crack |
| `-steam` | Force target platform — Steam |
| `-targetsavebuild <build>` | Override target BUILD for downgrade (RE9 only). Accepts hex (`0x01001000`) or alias (`v4`, `v5`, `v6`, `crack`, `steam`). Default: `0x01001002` |

### Examples

**RE9: Crack -> Steam** (auto-detect game by AppID):
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\3764200\remote\win64_save"
```

**RE9: with explicit game code**:
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\3764200\remote\win64_save" -re9
```

**Monster Hunter Wilds**:
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\2246340\remote\win64_save" -mhw
```

**Dragon's Dogma 2 (auto-detect)**:
```
save-convert.exe -821127145 -"C:\Program Files (x86)\Steam\userdata\821127145\2054970\remote\win64_save" -silent
```

**RE9: with custom target BUILD**:
```
save-convert.exe -76561197960287930 -"C:\Users\User\AppData\Roaming\GSE Saves\3764200\remote\win64_save" -re9 -targetsavebuild v4
```

### Auto-launch via Steam

You can set up automatic conversion on every game launch via Steam.

#### Option 1: via pre-launch-steam.cmd (recommended)

**Steam -> Library -> Right-click game -> Properties -> Launch Options:**

```
"C:\Tools\SaveCompat\pre-launch-steam.cmd" re9 %command%
```

Or without game code (auto-detect via %SteamAppId%):
```
"C:\Tools\SaveCompat\pre-launch-steam.cmd" %command%
```

Game codes for pre-launch-steam.cmd: `re9`, `mhw`, `dd2`, `dr`, `kg`, `mhs3`, `mmsf`, `pragmata`

#### Option 2: direct call

```
cmd /c start /wait "" "C:\Tools\SaveCompat\save-convert.exe" -YOUR_STEAM_ID -"C:\Program Files (x86)\Steam\userdata\YOUR_STEAM32\3764200\remote\win64_save" -re9 -silent & start "" %command%
```

### Where to find your Steam ID

Your Steam ID is a numeric account identifier. You can find it:
- On your Steam profile page (URL contains the ID)
- In emulator settings (Goldberg, GreenLuma, etc.)
- In crack configs (e.g., `ColdClientLoader.ini` -> `AccountId` parameter)

## BUILD Downgrade (RE9 only)

Downgrade is performed automatically when converting RE9 saves. If a file's BUILD is higher than the target (`0x01001002` by default), it's lowered. If the file's BUILD is equal or lower — nothing changes.

For other games, BUILD patching is not performed (re-sign only).

| BUILD | Version | Alias |
|-------|---------|-------|
| `0x01001000` | v1.0 (initial release) | `v4`, `crack` |
| `0x01001001` | v1.0.1 | — |
| `0x01001002` | v1.1 (April 2026 patch) | `v5`, `steam` |
| `0x01002000` | v2.0 (March 2026 Steam update) | `v6` |

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
