# Техническая документация — Game Save Convert v4.0

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
| `SavePatching.cs` | Детекция платформы (Steam/Crack), BUILD константы, валидация, даунгрейд логика |
| `RemoteCacheGenerator.cs` | Генерация remotecache.vdf для Steam Cloud Sync (+ read-only атрибут) |
| `SteamIds.cs` | Загрузка и парсинг steam_ids.txt (async, graceful timeout 1 сек) |
| `ProgressForm.cs` | WinForms окно прогресса brute-force |
| `Benchmark.cs` | Standalone тест скорости brute-force |

## Алгоритм работы

```
0. Очистка temp от предыдущих запусков
1. Команда benchmark? → Benchmark.Run() → exit 0
2. Парсинг аргументов (steam_id, путь, игра, -silent, -crack/-steam)
3. Детекция целевой платформы (автоматически по пути или принудительно через -crack/-steam)
4. Загрузка профиля RE9 из <InstallDir>\mandarin\_profiles\*.bin
5. Проверка папки сохранений (нет файлов → exit 0)
6. Тест: расшифровка testFile с targetId
   └─ Если успех → сейвы уже совместимы (+ remotecache.vdf для Steam) → exit 0
7. Попытка скачать steam_ids.txt (timeout 1 сек)
   ├─ Успех → list search по HeaderKey pre-filter (мгновенно)
   └─ Ошибка → лог "offline mode", идём дальше
8. Если не найден → ProgressForm + полный brute-force (0..4.3B)
   └─ Chunk-based: ~830M ID/sec (1 поток), ~5.9B ID/sec (все ядра)
   └─ Worst case: ~17 сек (1 поток), ~1 сек (12 ядер)
   └─ Отмена → Cleanup temp → exit 1
9. Валидация BUILD всех файлов (> BuildMaxSupported → abort exit 1)
10. Определение targetBuild: Crack → 0x01001000, Steam → null
11. Re-sign всех файлов во TEMP + BUILD patch при Crack target
    (ошибка → abort, оригиналы нетронуты)
12. data00-1.bin: re-sign + BUILD patch + version+2 (при даунгрейде)
13. Backup оригиналов (ошибка → abort, оригиналы нетронуты)
14. Копирование re-signed из temp в папку сохранений
15. remotecache.vdf (если Steam target) → read-only атрибут
16. Очистка старых бэкапов (keep 3) → Cleanup temp → exit 0
```

**Exit codes:** 0=успех, 1=ошибка/отмена/неподдерживаемый BUILD, 2=не найден

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

## Гибридный режим поиска source ID (v4)

```
1. Совместимы? (decrypt с target ID) → exit 0
2. Попытка скачать steam_ids.txt (timeout 1 сек)
   ├─ Успех → list search (мгновенно)
   └─ Ошибка → лог "offline mode"
3. Не найден или список пуст → brute-force (5-17 сек)
```

**Что убрано в v4 по сравнению с v3:**
- Whitelist проверка target ID (exit 3)
- Команда `check`
- `IdReporter.cs` — отправка ID на сервер
- `known_ids.txt` — локальный кэш (ПК заморожены, бесполезен)
- Обязательный интернет — без него v3 выходил с exit 1

## Система даунгрейда BUILD

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

targetBuild:
  Crack → 0x01001000 (каждый файл проверяется и патчится индивидуально)
  Steam → null (без даунгрейда)
```

## data00-1.bin — version counter

### Проблема

Поле `version` по смещению 0x28 в `data00-1.bin` — внутренний счётчик, НЕ версия формата. Игра увеличивает его на +2 каждый запуск. Файл с `version` ниже ожидаемого **отвергается** игрой (сброс на дефолтные настройки).

### Решение

При даунгрейде `data00-1.bin`: после патча BUILD, увеличить `version` на +2.

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
| **Crack → Steam** | auto (STEAM) или -steam | Без изменений | Re-sign | Генерируется (read-only) |
| **Steam → Crack** | auto (GSE) или -crack | Downgrade 0x01001002 → 0x01001000 | Re-sign + BUILD patch + version+2 | Не генерируется |
| **Crack → Crack** (другой ID) | auto (GSE) или -crack | Downgrade если build > 0x01001000 | Re-sign + patch если нужно | Не генерируется |
| **Steam → Steam** (другой ID) | auto (STEAM) или -steam | Без изменений | Re-sign | Генерируется (read-only) |
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

### Изменения в v4

- Убран consent panel (согласие на передачу Steam ID) — v4 ничего не отправляет
- Кнопка "Установить" сразу активна (без чекбокса)
- Версия 4.0 в заголовках и тексте
- Описание: "Быстрая конвертация сохранений между Steam ID без ограничений"

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
├── README.md          # Пользовательская документация
├── DOCS.md            # Техническая документация (этот файл)
├── TODO.md            # Roadmap
├── LICENSE            # MIT
├── steam_ids.txt      # База известных Steam ID
├── .gitignore         # Исключает исходники, бинарники, архивы
│
├── save-convert-v4/   # Исходники v4.0 (git-ignored)
├── installer-v4.cs    # Исходник установщика (git-ignored)
└── installer.exe      # Собранный установщик (git-ignored, в releases)
```

## Добавление нового Steam ID

1. Добавить в `steam_ids.txt` (Steam32 или Steam64)
2. Закоммитить и запушить в `main`
3. Подхватится автоматически при следующем запуске (с учётом cache-busting)

## Логирование

Лог: `<InstallDir>\save-convert.log`. Перезаписывается при каждом запуске.

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

Логируются: результат скачивания steam_ids.txt, результат list search, прогресс brute-force, шаги re-sign/backup/copy, BUILD patching, remotecache.vdf.
