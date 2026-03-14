# Техническая документация — Game Save Convert v3.0

## Архитектура

Единое .NET 10 приложение. MandarinJuiceCore используется как библиотека напрямую (без внешних CLI-процессов).

| Файл | Назначение | Runtime |
|------|-----------|---------|
| `save-convert.exe` | Основная утилита (+ встроенный brute-force) | .NET 10 |
| `installer.exe` | GUI/CLI-установщик | .NET Framework 4.x |

### Компоненты save-convert

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа, CLI-аргументы, оркестрация, команда `check` |
| `BruteForce.cs` | HeaderKey pre-filter + полный перебор |
| `SaveOperations.cs` | Decrypt/Encrypt/Re-sign/ReadSaveVersion/ProcessData001 через MandarinJuiceCore |
| `SavePatching.cs` | Детекция платформы (Steam/Crack), BUILD константы, валидация, даунгрейд логика |
| `RemoteCacheGenerator.cs` | Генерация remotecache.vdf для Steam Cloud Sync |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt (с cache-busting) |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `IdReporter.cs` | Отправка найденных ID в Google Forms |

## Алгоритм работы

```
0. Очистка temp от предыдущих запусков
1. Команда check? → RunCheck() → exit
2. Парсинг аргументов (steam_id, путь, игра, -silent, -crack/-steam)
3. Детекция целевой платформы (автоматически по пути или принудительно через -crack/-steam)
4. Загрузка профиля RE9 из C:\Tools\SaveCompat\mandarin\_profiles\*.bin
5. Проверка папки сохранений (нет файлов → exit 0)
6. Загрузка steam_ids.txt (HTTP GET + cache-busting)
   └─ Нет интернета → MessageBox + exit 1
7. Проверка targetId в авторизованном списке (нет → MessageBox + exit 3)
8. Тест: расшифровка testFile с targetId
   └─ Если успех → сейвы уже совместимы (+ remotecache.vdf для Steam) → exit 0
9. Поиск по списку: HeaderKey pre-filter для каждого ID
   └─ Мгновенно (~1мс на все ID из списка)
10. Если не найден → ProgressForm + полный brute-force (0..4.3B)
    └─ ~18M ID/sec, ~4 мин максимум
    └─ Отмена → Cleanup temp → exit 1
11. IdReporter.Report(id) — отправка в Google Forms
12. Валидация BUILD всех файлов (> BuildMaxSupported → abort exit 1)
13. Определение targetBuild: Crack → 0x01001000, Steam → null
14. Re-sign всех файлов во TEMP + BUILD patch при Crack target
    (ошибка → abort, оригиналы нетронуты)
15. data00-1.bin: re-sign + BUILD patch + version+2 (при даунгрейде)
16. Backup оригиналов (ошибка → abort, оригиналы нетронуты)
17. Копирование re-signed из temp в папку сохранений
18. remotecache.vdf (если Steam target)
19. Очистка старых бэкапов (keep 3) → Cleanup temp → exit 0
```

**Exit codes:** 0=успех, 1=ошибка/отмена/неподдерживаемый BUILD, 2=не найден, 3=целевой ID не авторизован

## Команда check

```
save-convert check <steam_id>
```

1. Загрузка steam_ids.txt
2. Проверка наличия ID в списке
3. Если найден → MessageBox "ID найден, конвертация возможна"
4. Если не найден → авто-отправка в Google Forms + MessageBox "Заявка отправлена, будет добавлен в течение дня"

## BruteForce — HeaderKey Pre-filter

Ключевая оптимизация. Вместо полной расшифровки каждого файла (~2300 ID/sec), используется проверка по HeaderKey (~18M ID/sec).

### Принцип

Первые 64 байта каждого slice header в зашифрованном файле — это HeaderKey (статическое значение из MandarinDeencryptor), XOR'd с потоком SplitMix64. HeaderKey одинаков для всех userId, а SplitMix64 поток зависит от userId. Зная HeaderKey и зашифрованные байты, можно предсказать ожидаемый SplitMix64 поток для правильного userId.

### Алгоритм

1. Извлечь `HeaderKey` через reflection из `MandarinDeencryptor`
2. Для тестового файла: предвычислить `stateAfterQueue` и `expectedXorBytes` (64 байта)
3. Для каждого кандидатного ID:
   - 16 вызовов SplitMix64 (CalculateHeaderChecksum)
   - Побайтовое сравнение с `expectedXorBytes` (DeencryptSliceHeader)
   - 99.6% отсеиваются по первому байту
4. Если pre-filter пройден → полная верификация через `MandarinDeencryptor.DecryptData`

### ParseVariant

| Значение | Формула | Игры |
|----------|---------|------|
| 0 | `steam64` | — |
| 1 | `~accountId \| 0xFFFFFFFF00000000` | — |
| 2 | `~steam64` | RE9 |
| 3 | `~obfuscated(steam64)` | — |

## Система даунгрейда BUILD

### Назначение

Steam-версия RE9 использует BUILD `0x01001002`, crack — `0x01001000`. Старая версия игры (crack) отказывается загружать сейвы с новым BUILD. При переносе Steam→Crack необходимо понизить BUILD.

### Смещения

| Тип файла | Смещение BUILD | Примеры |
|-----------|---------------|---------|
| data000.bin, *Slot*.bin | 0x5C | data000.bin, data000Slot0001AutoSave.bin |
| data00-1.bin | 0x4C | Настройки, назначения клавиш |

### Известные значения BUILD

| Значение | Описание |
|----------|----------|
| `0x01001000` | Старая версия (crack v4) |
| `0x01001001` | Промежуточная (некоторые файлы) |
| `0x01001002` | Новая версия (Steam v5) |

### Логика

```
SavePatching.DetectTarget(savePath):
  path содержит "STEAM" → SaveTarget.Steam
  path содержит "GSE"   → SaveTarget.Crack
  иначе → SaveTarget.Unknown (можно переопределить через -crack/-steam)

NeedsDowngrade(target, currentBuild):
  target == Crack AND currentBuild > BuildOldCrack → true

targetBuild:
  Crack → 0x01001000 (каждый файл проверяется и патчится индивидуально)
  Steam → null (без даунгрейда)
```

## data00-1.bin — version counter

### Проблема

Поле `version` по смещению 0x28 в `data00-1.bin` — внутренний счётчик, НЕ версия формата. Игра увеличивает его на +2 каждый запуск. Файл с `version` ниже ожидаемого **отвергается** игрой (сброс на дефолтные настройки — оконный режим, дефолтное управление).

### Решение

При даунгрейде `data00-1.bin`: после патча BUILD, увеличить `version` на +2.

```csharp
// ProcessData001():
if (patchVersion) {
    newVer = oldVer + 2;
    BitConverter.GetBytes(newVer).CopyTo(raw, 0x28);
}
```

Это гарантирует, что игра примет файл настроек после конвертации.

## remotecache.vdf

### Назначение

При переносе сохранений на Steam необходимо создать файл `remotecache.vdf`, чтобы Steam Cloud Sync распознал файлы. Без него Steam может перезаписать их облачной копией.

### Расположение

```
<userdata>/<steam32>/<appid>/
├── remotecache.vdf              ← генерируется здесь
└── remote/
    └── win64_save/
        ├── data000.bin
        └── ...
```

### Формат

Valve VDF формат. Для каждого `.bin` файла в папке сохранений:

```vdf
"3764200"
{
    "ChangeNumber"    "<count * 2>"
    "OSType"          "0"
    "win64_save/<filename>.bin"
    {
        "root"              "0"
        "size"              "<filesize>"
        "localtime"         "<unix_timestamp>"
        "time"              "<unix_timestamp>"
        "remotetime"        "<unix_timestamp>"
        "sha"               "<sha1_hex_lowercase>"
        "syncstate"         "4"
        "persiststate"      "0"
        "platformstosync2"  "-1"
    }
}
```

## Валидация BUILD

### Максимальный поддерживаемый BUILD

```csharp
public const uint BuildMaxSupported = 0x01001002;
public static bool IsBuildSupported(uint build) => build <= BuildMaxSupported;
```

### Логика

Перед re-sign всех файлов выполняется проверка BUILD каждого файла через `SaveOperations.ReadSaveVersion()`. Если хотя бы один файл имеет BUILD > `BuildMaxSupported`:
- MessageBox с ошибкой (на русском)
- Лог: `Unsupported build in <filename>: 0x<build>`
- Exit code 1

Это предотвращает тихую порчу сохранений при выходе новых версий игры с изменённым форматом.

## Таблица поведения по сценариям

| Сценарий | Детекция | BUILD | data00-1 | remotecache.vdf |
|----------|----------|-------|----------|-----------------|
| **Crack → Steam** | auto (STEAM) или -steam | Без изменений | Re-sign | Генерируется |
| **Steam → Crack** | auto (GSE) или -crack | Downgrade 0x01001002 → 0x01001000 | Re-sign + BUILD patch + version+2 | Не генерируется |
| **Crack → Crack** (другой ID) | auto (GSE) или -crack | Downgrade если build > 0x01001000 | Re-sign + patch если нужно | Не генерируется |
| **Steam → Steam** (другой ID) | auto (STEAM) или -steam | Без изменений | Re-sign | Генерируется |
| **Уже совместимы** | — | — | — | Генерируется (если Steam) |

## Безопасность данных

### Принцип: оригиналы неприкосновенны

1. **Перешифровка** выполняется только в `%TEMP%\save-compat-work\resign\`
2. Если ЛЮБОЙ файл не прошёл → abort, temp удаляется, оригиналы нетронуты
3. **Бэкап** создаётся только после успешной перешифровки ВСЕХ файлов
4. **Копирование** результатов из temp в папку сохранений — последний шаг
5. При ошибке копирования → сообщение с путём к бэкапу для ручного восстановления

### Очистка temp

- При старте: удаление leftover temp от предыдущих запусков/крашей
- При отмене: `Cleanup()` → удаление temp → exit 1
- При ошибке: `Die()` → `Cleanup()` → exit с кодом ошибки
- При успехе: `Cleanup()` → exit 0

## steam_ids.txt

**URL:** `https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt`

**Cache-busting:** к URL добавляется `?t=<unix_timestamp>` для обхода 5-минутного CDN-кэша GitHub.

**Формат:**
```
# Комментарий
22202                    # Steam32
76561197960287930         # Steam64
1 915 550 405            # С пробелами — допустимо
```

- Строки с `#` — комментарии
- Пробелы в числах удаляются автоматически
- Оба формата (Steam32 и Steam64) поддерживаются
- При загрузке нормализуются в Steam64, дедупликация через HashSet

**Конвертация:** `Steam64 = Steam32 + 76561197960265728`

## IdReporter — автосбор Steam ID

При обнаружении нового sourceId через brute-force, а также при команде `check` для неизвестного ID, автоматически отправляется в Google Forms:

- **URL:** Google Forms formResponse endpoint
- **Поля:** Game (код игры или "check"), Steam ID (Steam64)
- **Режим:** fire-and-forget, timeout 5 сек, все исключения проглатываются
- Данные попадают в Google Sheets для последующего добавления в `steam_ids.txt`

## Система защиты

### Двусторонняя проверка

1. **Target ID** (ваш): проверяется по `steam_ids.txt` ДО начала работы. Нет в списке → exit 3.
2. **Source ID** (исходный): сначала поиск по списку, затем brute-force. Не найден → exit 2.

### PickTestFile

Выбор файла для тестирования:
1. Приоритет: `data000.bin`
2. Затем: любой `*Slot*.bin`
3. **Избегает** `data00-1.bin` (баг ValidateFirstSlice в MandarinJuiceCore)

## Профили шифрования

| Файл | Код |
|------|-----|
| `Resident Evil 9 Requiem v1.bin` | `re9` |

## Бэкапы

**Формат имени:** `backup_<Steam32_src>_<Steam32_dst>_<yyyyMMdd_HHmmss>`

Содержит все `.bin` + `info.txt`. Ротация: максимум 3, старые удаляются.

## Установщик (installer.exe)

.NET Framework 4.x, компилируется как console app (`/target:exe`) для совместимости с CMD. В GUI-режиме вызывает `FreeConsole()`.

### Режимы

- **GUI**: запуск без аргументов → окно с чекбоксом согласия, прогрессом, логом
- **Silent**: `/s`, `/silent`, `/quiet`, `/q` → вывод в консоль, без окон, без открытия файлов

### Шаги установки

1. Создание `C:\Tools\SaveCompat\` и `mandarin\_profiles\`
2. Скачивание профиля RE9 (_profiles.zip)
3. Установка .NET 10 Desktop Runtime (прямая загрузка + winget fallback)
4. Скачивание save-convert.zip → распаковка в installDir
5. Добавление в системный PATH
6. Скачивание README.md локально

### Особенности

- Обязательный чекбокс согласия на передачу Steam ID (GUI)
- Авто-открытие локального README после установки (GUI)
- Вывод пути к README в консоль (Silent)
- Temp-файлы в installDir (не %TEMP%, т.к. SYSTEM не может писать в C:\Windows\TEMP)
- Повторный запуск безопасен (idempotent)
- UAC запрашивается автоматически (GUI)

## Сборка

### save-convert

```cmd
cd save-convert-v3
dotnet publish -c Release --self-contained false -o publish
```

Упаковать `publish/` (без .pdb) в `save-convert.zip`.

### installer.exe

```cmd
csc -nologo -target:exe -platform:x64 -reference:System.dll -reference:System.Drawing.dll -reference:System.Windows.Forms.dll -reference:System.IO.Compression.dll -reference:System.IO.Compression.FileSystem.dll -out:installer.exe installer-v3.cs
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
├── README.md          # Пользовательская документация
├── DOCS.md            # Техническая документация (этот файл)
├── TODO.md            # Roadmap
├── LICENSE            # MIT
├── steam_ids.txt      # База известных Steam ID
├── .gitignore         # Исключает исходники, бинарники, архивы
│
├── save-convert-v3/   # Исходники v3.0 (git-ignored)
├── installer-v3.cs    # Исходник установщика (git-ignored)
└── installer.exe      # Собранный установщик (git-ignored, в releases)
```

### Структура установки

```
C:\Tools\SaveCompat\
├── save-convert.exe              # Основная утилита
├── save-convert.dll              # Основная библиотека
├── MandarinJuiceCore.dll         # Движок шифрования
├── Mi5hmasH.*.dll                # Зависимости
├── save-convert.deps.json
├── save-convert.runtimeconfig.json
├── save-convert.log              # Лог
├── README.md                     # Инструкция
└── mandarin\
    └── _profiles\                # Профиль шифрования
        └── Resident Evil 9 Requiem v1.bin
```

## Логирование

Лог: `C:\Tools\SaveCompat\save-convert.log`. Перезаписывается при каждом запуске.

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

Логируются: загруженные ID (первые 10), результат list search, прогресс brute-force, шаги re-sign/backup/copy, BUILD patching, remotecache.vdf.

## Добавление нового Steam ID

### Автоматически
- `save-convert check <steam_id>` — проверка + авто-заявка
- Brute-force находит ID → авто-отправка через IdReporter

### Вручную
1. Добавить в `steam_ids.txt` (Steam32 или Steam64)
2. Закоммитить и запушить в `main`
3. Подхватится автоматически при следующем запуске (с учётом cache-busting)
