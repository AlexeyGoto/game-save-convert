# Техническая документация — Game Save Convert v2.0

## Архитектура

Единое .NET 10 приложение. MandarinJuiceCore используется как библиотека напрямую (без внешних CLI-процессов).

| Файл | Назначение | Runtime |
|------|-----------|---------|
| `save-convert.exe` | Основная утилита (+ встроенный brute-force) | .NET 10 |
| `installer.exe` | GUI-установщик | .NET Framework 4.x |

### Компоненты save-convert

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа, CLI-аргументы, оркестрация |
| `BruteForce.cs` | HeaderKey pre-filter + полный перебор |
| `SaveOperations.cs` | Decrypt/Encrypt/Re-sign через MandarinJuiceCore |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `IdReporter.cs` | Отправка найденных ID в Google Forms |

## Алгоритм работы

```
1. Парсинг аргументов (steam_id, путь, игра)
2. Загрузка профилей из C:\Tools\SaveCompat\mandarin\_profiles\*.bin
3. Фильтрация по игре (ResolveGameAlias: re9, mhw, dd2, dr, kg)
4. Проверка папки сохранений (нет файлов → exit 0)
5. Загрузка steam_ids.txt (HTTP GET в память)
6. Проверка targetId в авторизованном списке (нет → MessageBox + exit 3)
7. Тест: расшифровка testFile с targetId (MandarinJuiceCore напрямую)
   └─ Если успех → сейвы уже совместимы → exit 0
8. Поиск по списку: HeaderKey pre-filter для каждого ID
   └─ Мгновенно (~1мс на все ID из списка)
9. Если не найден → ProgressForm + полный brute-force (0..4.3B)
   └─ ~18M ID/sec, ~4 мин максимум
10. IdReporter.Report(id) — отправка в Google Forms
11. Backup → Re-sign всех файлов → exit 0
```

**Exit codes:** 0=успех, 1=ошибка, 2=не найден, 3=целевой ID не авторизован

## BruteForce — HeaderKey Pre-filter

Ключевая оптимизация v2.0. Вместо полной расшифровки каждого файла (~2300 ID/sec), используется проверка по HeaderKey (~18M ID/sec).

### Принцип

MandarinDeencryptor генерирует 64-байтный HeaderKey из userId через SplitMix64. Этот ключ XOR-ится с заголовком файла. Зная исходный заголовок (он фиксирован — `0x00` байты), можно предсказать первые 64 байта зашифрованного файла для любого userId.

### Алгоритм

1. Извлечь `HeaderKey` через reflection из `MandarinDeencryptor`
2. Для тестового файла: предвычислить `stateAfterQueue` и `expectedXorBytes` (64 байта)
3. Для каждого кандидатного ID:
   - 16 вызовов SplitMix64 (unrolled) → 8 байт каждый → 128 байт → берём первые 64
   - Побайтовое сравнение с `expectedXorBytes`
   - 99.6% отсеиваются по первому байту
4. Если pre-filter пройден → полная верификация через `MandarinDeencryptor.DecryptData`

### ParseVariant

| Значение | Формула | Игры |
|----------|---------|------|
| 0 | `steam64` | — |
| 1 | `~accountId \| 0xFFFFFFFF00000000` | — |
| 2 | `~steam64` | RE9 |
| 3 | `~obfuscated(steam64)` | — |

## steam_ids.txt

**URL:** `https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt`

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

При обнаружении нового sourceId через brute-force, ID автоматически отправляется в Google Forms:

- **URL:** Google Forms formResponse endpoint
- **Поля:** Game (код игры), Steam ID (Steam64)
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

| Файл | Код | Строка поиска |
|------|-----|--------------|
| `Resident Evil 9 Requiem v1.bin` | `re9` | `Resident Evil 9` |
| `Monster Hunter Wilds v1.bin` | `mhw` | `Monster Hunter Wilds` |
| `Dragon's Dogma 2 v1.bin` | `dd2` | `Dragon's Dogma 2` |
| `Dead Rising Deluxe Remaster v1.bin` | `dr` | `Dead Rising` |
| `Kunitsu-Gami Path of the Goddess v1.bin` | `kg` | `Kunitsu` |

## Бэкапы

**Формат имени:** `backup_<Steam32_src>_<Steam32_dst>_<yyyyMMdd_HHmmss>`

Содержит все `.bin` + `info.txt`. Ротация: максимум 3, старые удаляются.

## Установщик (installer.exe)

.NET Framework 4.x WinForms. Компилируется через `csc.exe`.

### Шаги

1. Создание `C:\Tools\SaveCompat\` и `mandarin\_profiles\`
2. Скачивание профилей игр (_profiles.zip)
3. Установка .NET 10 runtime (dotnet-install.ps1)
4. Скачивание save-convert.zip → распаковка в installDir
5. Добавление в системный PATH

### Особенности

- Обязательный чекбокс согласия на передачу Steam ID
- Кнопка "Открыть инструкцию" после установки
- Повторный запуск безопасен (idempotent)
- UAC запрашивается автоматически

## Сборка

### save-convert

```cmd
cd save-convert
dotnet publish -c Release --self-contained false -o publish
```

Упаковать `publish/` (без .pdb) в `save-convert.zip`.

### installer.exe

```cmd
csc -nologo -target:winexe -platform:x64 -reference:System.dll -reference:System.Drawing.dll -reference:System.Windows.Forms.dll -reference:System.IO.Compression.dll -reference:System.IO.Compression.FileSystem.dll -out:installer.exe installer.cs
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
├── LICENSE            # MIT
├── steam_ids.txt      # База известных Steam ID
├── .gitignore         # Исключает исходники, бинарники, архивы
│
├── save-convert/      # Исходники (git-ignored)
├── installer.cs       # Исходник установщика (git-ignored)
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
└── mandarin\
    └── _profiles\                # Профили шифрования
        ├── Resident Evil 9 Requiem v1.bin
        ├── Monster Hunter Wilds v1.bin
        └── ...
```

## Логирование

Лог: `C:\Tools\SaveCompat\save-convert.log`. Перезаписывается при каждом запуске.

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

## Добавление новой игры

1. Получить профиль `.bin` из MandarinJuice
2. Добавить алиас в `ResolveGameAlias()` в `Program.cs`
3. Пересобрать и залить в releases + обновить профили

## Добавление нового Steam ID

1. Добавить в `steam_ids.txt` (Steam32 или Steam64)
2. Закоммитить и запушить в `main`
3. Подхватится автоматически при следующем запуске
