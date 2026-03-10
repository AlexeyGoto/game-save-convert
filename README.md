# Game Save Convert

Tool for converting game save files between different Steam IDs. Automatically decrypts saves signed with an unknown Steam ID and re-encrypts them with your current ID.

## How it works

Many cracked games encrypt save files using the player's Steam ID as part of the encryption key. When saves come from a different source (another PC, a different crack/repack), they become unreadable because the Steam ID doesn't match.

This tool:
1. Downloads the latest list of known Steam IDs
2. Tries to decrypt saves with each known ID (brute-force)
3. Once the source ID is found, re-signs saves with your target Steam ID
4. Creates a backup before any changes

## Quick Install

Run in PowerShell (as Administrator):

```powershell
irm https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/install.ps1 | iex
```

This will:
- Download and extract [MandarinJuice](https://github.com/mi5hmash/MandarinJuice) CLI
- Install .NET 10 runtime if needed
- Install `save-convert.exe` and add it to PATH

## Usage

```
save-convert.exe -<target_steam_id> -<save_folder_path>
```

### Example

```
save-convert.exe -76561197960287930 -"C:\Users\User\AppData\Roaming\GSE Saves\3764200\remote\win64_save"
```

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | OK — saves are compatible or were converted successfully |
| 1 | Error — missing tools, network error, invalid arguments |
| 2 | Source Steam ID not found in the known list |

### Backups

Before converting, a backup is created in the save folder:

```
backup_<source_id>_<target_id>_<datetime>/
├── info.txt          # Conversion details
├── data00-1.bin      # Original save files
└── ...
```

Only the 3 most recent backups are kept; older ones are deleted automatically.

## Updating the Steam ID list

The file `steam_ids.txt` in this repository contains known Steam IDs used by various cracks and repacks. The tool downloads it fresh from GitHub on every run.

To add new IDs: edit `steam_ids.txt`, commit and push. All installations will pick up the changes automatically.

## Supported games

Any game supported by [MandarinJuice](https://github.com/mi5hmash/MandarinJuice) (Mandarin encryption / RE Engine saves):
- Resident Evil Village
- Resident Evil 9
- Devil May Cry 5
- Other RE Engine titles

## Third-party

This tool uses [MandarinJuice](https://github.com/mi5hmash/MandarinJuice) by [mi5hmash](https://github.com/mi5hmash) for save file decryption and re-signing.
MandarinJuice is licensed under the [MIT License](https://github.com/mi5hmash/MandarinJuice/blob/master/LICENSE).

## License

MIT License — see [LICENSE](LICENSE).
