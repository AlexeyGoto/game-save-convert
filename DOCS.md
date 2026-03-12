# Техническая документация — Game Save Convert v2.2

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
| `SaveOperations.cs` | Decrypt/Encrypt/Re-sign через MandarinJuiceCore |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt (с cache-busting) |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `IdReporter.cs` | Отправка найденных ID в Google Forms |

## Алгоритм работы

```
0. Очистка temp от предыдущих запусков
1. Команда check? → RunCheck() → exit
2. Парсинг аргументов (steam_id, путь, игра, -silent)
3. Загрузка профилей из C:\Tools\SaveCompat\mandarin\_profiles\*.bin
4. Фильтрация по игре (ResolveGameAlias: re9, mhw, dd2, dr, kg)
5. Проверка папки сохранений (нет файлов → exit 0)
6. Загрузка steam_ids.txt (HTTP GET + cache-busting)
   └─ Нет интернета → MessageBox + exit 1
7. Проверка targetId в авторизованном списке (нет → MessageBox + exit 3)
8. Тест: расшифровка testFile с targetId
   └─ Если успех → сейвы уже совместимы → exit 0
9. Поиск по списку: HeaderKey pre-filter для каждого ID
   └─ Мгновенно (~1мс на все ID из списка)
10. Если не найден → ProgressForm + полный brute-force (0..4.3B)
    └─ ~18M ID/sec, ~4 мин максимум
    └─ Отмена → Cleanup temp → exit 1
11. IdReporter.Report(id) — отправка в Google Forms
12. Re-sign всех файлов во TEMP (ошибка → abort, оригиналы нетронуты)
13. Backup оригиналов (ошибка → abort, оригиналы нетронуты)
14. Копирование re-signed из temp в папку сохранений
15. Очистка старых бэкапов (keep 3) → Cleanup temp → exit 0
```

**Exit codes:** 0=успех, 1=ошибка/отмена, 2=не найден, 3=целевой ID не авторизован

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

.NET Framework 4.x, компилируется как console app (`/target:exe`) для совместимости с CMD. В GUI-режиме вызывает `FreeConsole()`.

### Режимы

- **GUI**: запуск без аргументов → окно с чекбоксом согласия, прогрессом, логом
- **Silent**: `/s`, `/silent`, `/quiet`, `/q` → вывод в консоль, без окон, без открытия файлов

### Шаги установки

1. Создание `C:\Tools\SaveCompat\` и `mandarin\_profiles\`
2. Скачивание профилей игр (_profiles.zip)
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
cd save-convert
dotnet publish -c Release --self-contained false -o publish
```

Упаковать `publish/` (без .pdb) в `save-convert.zip`.

### installer.exe

```cmd
csc -nologo -target:exe -platform:x64 -reference:System.dll -reference:System.Drawing.dll -reference:System.Windows.Forms.dll -reference:System.IO.Compression.dll -reference:System.IO.Compression.FileSystem.dll -out:installer.exe installer.cs
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
├── README.md                     # Инструкция
└── mandarin\
    └── _profiles\                # Профили шифрования
        ├── Resident Evil 9 Requiem v1.bin
        ├── Monster Hunter Wilds v1.bin
        └── ...
```

## Логирование

Лог: `C:\Tools\SaveCompat\save-convert.log`. Перезаписывается при каждом запуске.

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

Логируются: загруженные ID (первые 10), результат list search, прогресс brute-force, шаги re-sign/backup/copy.

## Добавление новой игры

1. Получить профиль `.bin` из MandarinJuice
2. Добавить алиас в `ResolveGameAlias()` в `Program.cs`
3. Пересобрать и залить в releases + обновить профили

## Добавление нового Steam ID

### Автоматически
- `save-convert check <steam_id>` — проверка + авто-заявка
- Brute-force находит ID → авто-отправка через IdReporter

### Вручную
1. Добавить в `steam_ids.txt` (Steam32 или Steam64)
2. Закоммитить и запушить в `main`
3. Подхватится автоматически при следующем запуске (с учётом cache-busting)
