# Техническая документация — Game Save Convert v4.3

## Архитектура

Единое .NET 10 приложение. MandarinJuiceCore используется как библиотека напрямую (без внешних CLI-процессов).

| Файл | Назначение | Runtime |
|------|-----------|---------|
| `save-convert.exe` | Основная утилита (+ встроенный brute-force) | .NET 10 |
| `installer.exe` | GUI/CLI-установщик | .NET Framework 4.x |

### Компоненты save-convert

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа, CLI-аргументы, оркестрация |
| `BruteForce.cs` | Chunk-based parallel HeaderKey pre-filter + полный перебор |
| `SaveOperations.cs` | Decrypt/Encrypt/Re-sign/ReadSaveVersion/ProcessData001 через MandarinJuiceCore |
| `SavePatching.cs` | Детекция платформы (Steam/Crack), BUILD константы, KnownBuildVersions, DefaultTargetBuild |
| `RemoteCacheGenerator.cs` | Генерация remotecache.vdf для Steam Cloud Sync (+ read-only атрибут) |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt (async, graceful timeout 1 сек) |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `Benchmark.cs` | Standalone тест скорости brute-force |

## Алгоритм работы

```
0. Очистка temp от предыдущих запусков
1. Команда benchmark? → Benchmark.Run() → exit 0
2. Парсинг аргументов (steam_id, путь, игра, -silent, -crack/-steam, -targetsavebuild)
3. targetBuild = DefaultTargetBuild (0x01001002), или переопределённый через -targetsavebuild
4. Детекция целевой платформы (автоматически по пути или принудительно через -crack/-steam)
5. Загрузка профиля RE9 из <InstallDir>\mandarin\_profiles\*.bin
6. Проверка папки сохранений (нет файлов → exit 0)
7. Тест: расшифровка testFile с targetId
   └─ Если успех → сейвы уже совместимы (+ remotecache.vdf для Steam) → exit 0
8. Попытка скачать steam_ids.txt (timeout 1 сек)
   ├─ Успех → list search по HeaderKey pre-filter (мгновенно)
   └─ Ошибка → лог "offline mode", идём дальше
9. Если не найден → ProgressForm + полный brute-force (0..4.3B)
   └─ Отмена → Cleanup temp → exit 1
10. Re-sign всех файлов во TEMP + BUILD downgrade (если curBuild > targetBuild)
    (ошибка → abort, оригиналы нетронуты)
11. data00-1.bin: re-sign + BUILD downgrade + version+2 (только если BUILD реально понижен)
12. Backup оригиналов (ошибка → abort, оригиналы нетронуты)
13. Копирование re-signed из temp в папку сохранений
14. remotecache.vdf (если Steam target) → read-only атрибут
15. Очистка старых бэкапов (keep 3) → Cleanup temp → exit 0
```

**Exit codes:** 0=успех, 1=ошибка/отмена, 2=не найден

## BruteForce — Chunk-based Parallel Pre-filter (v4)

Ключевая оптимизация v4. Вместо Interlocked.Increment на каждой итерации — chunk-based параллелизм.

### Производительность

| Метрика | v3.0 | v4.0 |
|---------|------|------|
| Throughput (1 поток) | ~18M ID/sec | ~830M ID/sec |
| Throughput (все ядра) | N/A | ~5.9B ID/sec (12 ядер) |
| Worst case (полный скан) | ~4 мин | ~1-17 сек |

### Архитектура

- **Chunk size:** 65,536 (64K) IDs
- **Interlocked:** 1 раз на чанк (а не на каждый ID)
- **Progress report:** каждые 128 чанков (~8M IDs)
- **Hot loop:** без аллокаций, без Interlocked, без progress callback
- **Early exit:** `loopState.Stop()` при нахождении

### HeaderKey Pre-filter

Первые 64 байта каждого slice header в зашифрованном файле — это HeaderKey (статическое значение из MandarinDeencryptor), XOR'd с потоком SplitMix64. HeaderKey одинаков для всех userId, а SplitMix64 поток зависит от userId.

1. Извлечь `HeaderKey` через reflection из `MandarinDeencryptor`
2. Предвычислить `stateAfterQueue` и `expectedXorBytes` (64 байта)
3. Для каждого кандидатного ID:
   - 16 вызовов SplitMix64 (CalculateHeaderChecksum)
   - Побайтовое сравнение (первый байт отсеивает 99.6%)
4. Если pre-filter пройден → полная верификация через `MandarinDeencryptor.DecryptData`

### ParseVariant

| Значение | Формула | Игры |
|----------|---------|------|
| 0 | `steam64` | — |
| 1 | `~accountId \| 0xFFFFFFFF00000000` | — |
| 2 | `~steam64` | RE9 |
| 3 | `~obfuscated(steam64)` | — |

## Система даунгрейда BUILD (v4.3)

### Изменения в v4.3

- **Автоматический**: targetBuild всегда задан (по умолчанию `DefaultTargetBuild = 0x01001002`)
- **Только даунгрейд**: `curBuild > targetBuild` → патч вниз. Если `curBuild <= targetBuild` → без изменений
- **`-targetsavebuild`**: опциональное переопределение целевого BUILD
- **Version patch**: `data00-1.bin` version+2 только если BUILD был реально понижен

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

## data00-1.bin — version counter

### Проблема

Поле `version` по смещению 0x28 в `data00-1.bin` — внутренний счётчик, НЕ версия формата. Игра увеличивает его на +2 каждый запуск. Файл с `version` ниже ожидаемого **отвергается** игрой (сброс на дефолтные настройки).

### Решение

При даунгрейде `data00-1.bin`: если BUILD был реально понижен, увеличить `version` на +2.

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

### Защита от перезаписи (v4)

После записи `remotecache.vdf` устанавливается атрибут `ReadOnly`:
```csharp
File.SetAttributes(vdfPath, FileAttributes.ReadOnly);
```

Перед перезаписью при повторном запуске — атрибут снимается.

## steam_ids.txt

**URL:** `https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt`

**Cache-busting:** к URL добавляется `?t=<unix_timestamp>` для обхода CDN-кэша GitHub.

**Timeout:** 1 секунда. При любой ошибке (сеть, timeout, парсинг) — graceful fallback на brute-force.

**Формат:**
```
# Комментарий
22202                    # Steam32
76561197960287930         # Steam64
1 915 550 405            # С пробелами — допустимо
```

## Таблица поведения по сценариям

| Сценарий | Детекция | BUILD | data00-1 | remotecache.vdf |
|----------|----------|-------|----------|-----------------|
| **Crack → Steam** | auto (STEAM) или -steam | Downgrade если > target | Re-sign + patch если downgraded | Генерируется (read-only) |
| **Steam → Crack** | auto (GSE) или -crack | Downgrade если > target | Re-sign + BUILD patch + version+2 если downgraded | Не генерируется |
| **Crack → Crack** (другой ID) | auto (GSE) или -crack | Downgrade если > target | Re-sign + patch если downgraded | Не генерируется |
| **Steam → Steam** (другой ID) | auto (STEAM) или -steam | Downgrade если > target | Re-sign + patch если downgraded | Генерируется (read-only) |
| **Уже совместимы** | — | — | — | Генерируется (если Steam) |

## Безопасность данных

### Принцип: оригиналы неприкосновенны

1. **Перешифровка** выполняется только в `%TEMP%\save-compat-work\resign\`
2. Если ЛЮБОЙ файл не прошёл → abort, temp удаляется, оригиналы нетронуты
3. **Бэкап** создаётся только после успешной перешифровки ВСЕХ файлов
4. **Копирование** результатов из temp в папку сохранений — последний шаг
5. При ошибке копирования → сообщение с путём к бэкапу для ручного восстановления

## Установщик (installer.exe)

.NET Framework 4.x, компилируется как console app (`/target:exe`) для совместимости с CMD. В GUI-режиме вызывает `FreeConsole()`.

### Режимы

- **GUI**: запуск без аргументов → окно с прогрессом и логом, кнопка "Установить" сразу активна
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

Логируются: результат скачивания steam_ids.txt, результат list search, прогресс brute-force, шаги re-sign/backup/copy, BUILD patching, remotecache.vdf.
