# Техническая документация — Game Save Convert v5.0

## Архитектура

Единое .NET 10 приложение. MandarinJuiceCore используется как библиотека напрямую (без внешних CLI-процессов).

| Файл | Назначение | Runtime |
|------|-----------|---------|
| `save-convert.exe` | Основная утилита (+ встроенный brute-force) | .NET 10 |
| `installer.exe` | GUI/CLI-установщик | .NET Framework 4.x |

### Компоненты save-convert

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа, CLI-аргументы, оркестрация, авто-детекция игры |
| `BruteForce.cs` | Chunk-based parallel HeaderKey pre-filter + полный перебор |
| `SaveOperations.cs` | Decrypt/Encrypt/Re-sign/ReadSaveVersion/ProcessData001 через MandarinJuiceCore |
| `SavePatching.cs` | Детекция платформы (Steam/Crack), BUILD константы, RE9AppId, SupportsBuildPatching |
| `RemoteCacheGenerator.cs` | Генерация remotecache.vdf для Steam Cloud Sync (+ read-only атрибут) |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt (async, graceful timeout 1 сек) |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `Benchmark.cs` | Standalone тест скорости brute-force |

## Поддерживаемые игры

| Игра | AppID | Алиасы | BUILD patching |
|------|-------|--------|----------------|
| Resident Evil 9 Requiem | 3764200 | `re9`, `requiem` | Да (авто-даунгрейд) |
| Dragon's Dogma 2 | 2054970 | `dd2`, `dogma2` | Нет (только re-sign) |
| Monster Hunter Wilds | 2246340 | `mhw`, `wilds` | Нет |
| Monster Hunter Stories 3 | 2852190 | `mhs3`, `stories3` | Нет |
| Dead Rising Deluxe Remaster | 2527390 | `dr`, `deadrising` | Нет |
| Kunitsu-Gami | 2510710 | `kg`, `kunitsu` | Нет |
| PRAGMATA | 3357650 | `pragmata` | Нет |
| Mega Man Star Force | 3500390 | `mmsf`, `megaman`, `starforce` | Нет |

## Алгоритм работы (v5.0)

```
0. Очистка temp от предыдущих запусков
1. Команда benchmark? → Benchmark.Run() → exit 0
2. Парсинг аргументов (steam_id, путь, [-game], -silent, -crack/-steam, -targetsavebuild)
3. Авто-детекция AppID из пути (TryExtractAppIdFromPath)
4. Резолв профилей:
   - Если -game указан → ResolveGameAlias → фильтр по имени файла профиля
   - Если AppID обнаружен → фильтр по profile.AppId
   - Иначе → загрузить все профили
5. BUILD patching gating:
   - Если -targetsavebuild указан → effectiveTargetBuild = указанный
   - Если SupportsBuildPatching(resolvedAppId) [RE9] → effectiveTargetBuild = DefaultTargetBuild
   - Иначе → effectiveTargetBuild = null (без BUILD patching)
6. Проверка папки сохранений (нет файлов → exit 0)
7. Тест: расшифровка testFile с targetId
   └─ Если успех → сейвы уже совместимы (+ remotecache.vdf для Steam) → exit 0
8. Попытка скачать steam_ids.txt (timeout 1 сек)
   ├─ Успех → list search по HeaderKey pre-filter (мгновенно)
   └─ Ошибка → лог "offline mode", идём дальше
9. Если не найден → ProgressForm + полный brute-force (0..4.3B)
   └─ Отмена → Cleanup temp → exit 1
10. Version check + silent mode graceful:
    - Если silentMode && неизвестный BUILD → effectiveTargetBuild = null
11. Re-sign всех файлов во TEMP + BUILD downgrade (если effectiveTargetBuild != null)
    (ошибка → abort, оригиналы нетронуты)
12. data00-1.bin: re-sign + BUILD downgrade + version+2 (только если BUILD реально понижен)
13. Backup оригиналов (ошибка → abort, оригиналы нетронуты)
14. Копирование re-signed из temp в папку сохранений
15. remotecache.vdf (если Steam target, с динамическим AppID) → read-only атрибут
16. Очистка старых бэкапов (keep 3) → Cleanup temp → exit 0
```

**Exit codes:** 0=успех, 1=ошибка/отмена, 2=не найден

## Авто-детекция AppID (TryExtractAppIdFromPath)

Извлекает AppID из пути к сохранениям:
- Steam: `userdata/<steam32>/<appid>/remote/...` → appid (позиция +2 после "userdata")
- Crack: `GSE Saves/<appid>/remote/...` → appid (позиция +1 после "GSE Saves")

Это позволяет сделать параметр `-game` опциональным.

## BUILD patching gate (SupportsBuildPatching)

BUILD patching (даунгрейд) выполняется **только для RE9** (AppID 3764200), так как:
- Константы BUILD специфичны для RE9
- Смещения BUILD в файлах могут отличаться между играми
- Для других игр безопасно выполнять только re-sign

Исключение: если пользователь явно указал `-targetsavebuild` — BUILD patching будет применён к любой игре.

## BruteForce — Chunk-based Parallel Pre-filter

### Производительность

| Метрика | Значение |
|---------|----------|
| Throughput (1 поток) | ~830M ID/sec |
| Throughput (все ядра) | ~5.9B ID/sec (12 ядер) |
| Worst case (полный скан) | ~1-17 сек |

### Архитектура

- **Chunk size:** 65,536 (64K) IDs
- **Interlocked:** 1 раз на чанк (а не на каждый ID)
- **Progress report:** каждые 128 чанков (~8M IDs)
- **Hot loop:** без аллокаций, без Interlocked, без progress callback
- **Early exit:** `loopState.Stop()` при нахождении

### ParseVariant

| Значение | Формула | Игры |
|----------|---------|------|
| 0 | `steam64` | — |
| 1 | `~accountId \| 0xFFFFFFFF00000000` | — |
| 2 | `~steam64` | RE9 |
| 3 | `~obfuscated(steam64)` | — |

## Система даунгрейда BUILD (RE9)

### Соответствие BUILD и версий

| BUILD | Версия | Алиас |
|-------|--------|-------|
| `0x01001000` | v1.0 (initial release) | `v4`, `crack` |
| `0x01001001` | v1.0.1 | — |
| `0x01001002` | v1.1 (April 2026 patch) | `v5`, `steam` |
| `0x01002000` | v2.0 (March 2026 Steam update) | `v6` |

### Смещения

| Тип файла | Смещение BUILD | Примеры |
|-----------|---------------|---------|
| data000.bin, *Slot*.bin | 0x5C | data000.bin, data000Slot0001AutoSave.bin |
| data00-1.bin | 0x4C | Настройки, назначения клавиш |

## remotecache.vdf

### Расположение

```
<userdata>/<steam32>/<appid>/
├── remotecache.vdf              ← генерируется здесь (read-only)
└── remote/
    └── win64_save/
        ├── data000.bin
        └── ...
```

AppID передаётся динамически из resolvedAppId (по умолчанию 3764200 для обратной совместимости).

### Защита от перезаписи

После записи `remotecache.vdf` устанавливается атрибут `ReadOnly`. Перед перезаписью при повторном запуске — атрибут снимается.

## pre-launch-steam.cmd

Поддерживает все 8 игр + авто-детекцию через `%SteamAppId%`.

При вызове без кода игры использует переменную `%SteamAppId%`, которую Steam автоматически устанавливает при запуске.

## Установщик (installer.exe)

.NET Framework 4.x, компилируется как console app (`/target:exe`) для совместимости с CMD. В GUI-режиме вызывает `FreeConsole()`.

URL профилей: `https://github.com/mi5hmash/MandarinJuice/releases/latest/download/_profiles.zip` — всегда актуальная версия.

### Режимы

- **GUI**: запуск без аргументов → окно с прогрессом и логом
- **Silent**: `/s`, `/silent`, `/quiet`, `/q` → вывод в консоль, без окон

## Сборка

### save-convert

```cmd
cd save-convert-v4
dotnet publish -c Release --self-contained false -o publish
```

Упаковать `publish/` (без .pdb) в `save-convert.zip`.

### installer.exe

```cmd
csc -nologo -target:exe -platform:x64 -reference:System.dll -reference:System.Drawing.dll -reference:System.Windows.Forms.dll -reference:System.IO.Compression.dll -reference:System.IO.Compression.FileSystem.dll -out:installer.exe installer-v4.cs
```

### Релиз

Файлы в GitHub Releases:
```
installer.exe        — единственный файл для скачивания пользователем
save-convert.zip     — скачивается автоматически установщиком
```

## Структура репозитория

```
game-save-convert/
├── README.md              # Пользовательская документация (русский)
├── README_EN.md           # Пользовательская документация (английский)
├── DOCS.md                # Техническая документация (этот файл)
├── LICENSE                # MIT
├── .gitignore
├── steam_ids.txt          # База известных Steam ID
├── installer-v4.cs        # Исходник установщика
├── pre-launch-steam.cmd   # Скрипт автозапуска для Steam
└── save-convert-v4/       # Исходники основной утилиты
    ├── save-convert-v4.csproj
    ├── Program.cs
    ├── SaveOperations.cs
    ├── SavePatching.cs
    ├── BruteForce.cs
    ├── Benchmark.cs
    ├── ProgressForm.cs
    ├── RemoteCacheGenerator.cs
    └── SteamIds.cs
```

## Добавление нового Steam ID

1. Добавить в `steam_ids.txt` (Steam32 или Steam64)
2. Закоммитить и запушить в `main`
3. Подхватится автоматически при следующем запуске (с учётом cache-busting)

## Логирование

Лог: `<InstallDir>\save-convert.log`. Перезаписывается при каждом запуске.

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

Логируются: авто-детекция AppID, resolved профили, BUILD patching status, результат скачивания steam_ids.txt, результат list search, прогресс brute-force, шаги re-sign/backup/copy, remotecache.vdf.
