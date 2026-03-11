# Техническая документация — Game Save Convert

## Архитектура

Проект состоит из двух исполняемых файлов:

| Файл | Назначение | Исходник |
|------|-----------|----------|
| `save-convert.exe` | Основная утилита конвертации | `save-convert.cs` |
| `installer.exe` | GUI-установщик | `installer.cs` |

Оба компилируются через .NET Framework 4.x (`csc.exe`), не требуют SDK.

## Компоненты системы

### save-convert.exe

Консольное приложение (скомпилировано как `/target:winexe` — без окна консоли). Запускается с аргументами командной строки, работает полностью автоматически.

**Зависимости runtime:**
- .NET Framework 4.x (встроен в Windows)
- `user32.dll` (P/Invoke для MessageBox)
- MandarinJuice CLI (внешний инструмент, `.NET 10 runtime`)

### installer.exe

WinForms-приложение. Автоматически запрашивает права администратора (UAC). Скачивает и устанавливает все зависимости.

**Зависимости runtime:**
- .NET Framework 4.x
- System.Windows.Forms, System.Drawing
- System.IO.Compression (распаковка ZIP)
- Доступ к интернету (GitHub)

### MandarinJuice CLI

Сторонний инструмент от [mi5hmash](https://github.com/mi5hmash/MandarinJuice). Выполняет фактическую расшифровку/шифрование сохранений.

**Режимы использования:**
- Decrypt: `-m d -g <profile> -p <dir> -u <steam_id>`
- Re-sign: `-m r -g <profile> -p <dir> -uI <source_id> -uO <target_id>`
- Help: `-h`

**Выходные данные:** создаёт папку `_OUTPUT` рядом со своим exe.

## Алгоритм работы save-convert.exe

```
1. Парсинг аргументов (steam_id, путь, игра)
2. Конвертация Steam ID в формат Steam64
3. Валидация путей (MandarinJuice, профили, папка сейвов)
4. Загрузка steam_ids.txt в память (HTTP GET → строка)
5. Проверка целевого ID по авторизованному списку
   └─ Если нет → MessageBox "несовместимы" → exit 3
6. Тест: расшифровка первого .bin файла целевым ID (во временной папке)
   └─ Если успех → сейвы уже совместимы, ничего не меняем → exit 0
7. Перебор: все ID × профиль указанной игры (brute-force)
   └─ Если найден → sourceId определён
   └─ Если не найден → MessageBox "несовместимы" → exit 2
8. Создание бэкапа (backup_<src>_<dst>_<timestamp>/)
9. Удаление старых бэкапов (оставить 3)
10. Re-sign: MandarinJuice перешифровывает все .bin
11. Копирование результата из _OUTPUT обратно в папку сейвов
12. Очистка временных файлов → exit 0
```

## steam_ids.txt

Текстовый файл с известными Steam ID. Хранится в репозитории, загружается в runtime через HTTP.

**Формат:**
```
# Комментарий
22202                    # Steam32
76561197960287930         # Steam64 (то же самое)
```

- Одна строка = один ID
- Строки с `#` — комментарии
- Поддерживаются оба формата (Steam32 и Steam64)
- При загрузке все ID нормализуются в Steam64
- Дубликаты автоматически удаляются через HashSet

**Конвертация:**
```
Steam64 = Steam32 + 76561197960265728
Steam32 = Steam64 - 76561197960265728
```

**URL:** `https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/steam_ids.txt`

Файл загружается через `WebClient.DownloadString()` — в память, без создания файла на диске.

## Система защиты

### Двусторонняя проверка ID

1. **Target ID** (ваш): проверяется по `steam_ids.txt` ДО начала работы. Если отсутствует → exit 3.
2. **Source ID** (исходный): определяется перебором ТОЛЬКО среди ID из `steam_ids.txt`. Если ни один не подошёл → exit 2.

Это означает:
- Нельзя расшифровать сейвы, зашифрованные неизвестным ID
- Нельзя зашифровать сейвы под ID, которого нет в списке
- Для добавления нового ID нужен коммит в репозиторий

### MessageBox при ошибках

При кодах 2 и 3 показывается нативный Windows MessageBox (через P/Invoke `user32.dll`). Текст сообщения:
- **Код 2:** «Сохранения несовместимы. Не удалось определить исходный аккаунт сохранений.»
- **Код 3:** «Сохранения несовместимы. Ваш аккаунт не найден в авторизованном списке.»

Steam ID в сообщении **не отображается**.

## Профили шифрования игр

Каждая игра использует свой профиль шифрования (`.bin` файл от MandarinJuice).

| Файл профиля | Код игры | Поисковая строка |
|--------------|----------|-----------------|
| `Resident Evil 9 Requiem v1.bin` | `re9` | `Resident Evil 9` |
| `Monster Hunter Wilds v1.bin` | `mhw` | `Monster Hunter Wilds` |
| `Dragon's Dogma 2 v1.bin` | `dd2` | `Dragon's Dogma 2` |
| `Dead Rising Deluxe Remaster v1.bin` | `dr` | `Dead Rising` |
| `Kunitsu-Gami Path of the Goddess v1.bin` | `kg` | `Kunitsu` |

Алиасы определяются в `ResolveGameAlias()`. Профиль фильтруется по подстроке в имени файла (case-insensitive).

## Система бэкапов

**Формат имени:** `backup_<Steam32_src>_<Steam32_dst>_<yyyyMMdd_HHmmss>`

**Содержимое:**
- Все оригинальные `.bin` файлы из папки сохранений
- `info.txt` — метаданные (исходный ID, целевой ID, профиль, дата, список файлов)

**Ротация:** хранится максимум 3 бэкапа. При создании нового — самый старый удаляется (сортировка по имени, т.е. по дате).

## Установщик (installer.exe)

### Шаги установки

1. Создание `C:\Tools\SaveCompat\` и `mandarin\`
2. Скачивание MandarinJuice CLI (ZIP с GitHub releases)
3. Скачивание профилей игр (ZIP с GitHub releases)
4. Установка .NET 10 runtime через `dotnet-install.ps1`
5. Проверка работоспособности MandarinJuice (`-h`)
6. Скачивание `save-convert.exe` из GitHub releases
7. Добавление `C:\Tools\SaveCompat` в системный PATH

### Idempotency

Установщик безопасен для повторного запуска:
- MandarinJuice не перекачивается, если `mandarin-juice-cli.exe` уже существует
- Профили не перекачиваются, если `.bin` файлы уже есть в `_profiles\`
- PATH не дублируется, если путь уже есть

### UAC

При запуске без прав администратора автоматически перезапускается с UAC-промптом (`runas`). Если пользователь отказывает — показывается MessageBox.

## Сборка

### Требования
- Windows с .NET Framework 4.x (обычно предустановлен)
- `csc.exe` из `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`

### Команды сборки

**save-convert.exe:**
```cmd
csc /nologo /target:winexe /out:save-convert.exe save-convert.cs
```

**installer.exe:**
```cmd
csc /nologo /target:winexe /out:installer.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll installer.cs
```

### Релиз

Оба `.exe` размещаются в GitHub Releases. URL для автоскачивания:
```
https://github.com/AlexeyGoto/game-save-convert/releases/latest/download/save-convert.exe
https://github.com/AlexeyGoto/game-save-convert/releases/latest/download/installer.exe
```

## Добавление новой игры

1. Получить профиль шифрования `.bin` из MandarinJuice
2. Положить в `C:\Tools\SaveCompat\mandarin\_profiles\`
3. Добавить алиас в `ResolveGameAlias()` в `save-convert.cs`
4. Пересобрать и залить в releases

## Добавление нового Steam ID

1. Добавить ID (Steam32 или Steam64) в `steam_ids.txt`
2. Закоммитить и запушить в `main`
3. Изменение подхватится автоматически при следующем запуске `save-convert.exe`

## Логирование

Лог пишется в `C:\Tools\SaveCompat\save-convert.log`. Перезаписывается при каждом запуске (начинается с `===== START =====`).

**Формат:** `YYYY-MM-DD HH:mm:ss <сообщение>`

**Что логируется:**
- Входные параметры (ID, путь, игра)
- Найденные профили
- Результат проверки MandarinJuice
- Количество файлов сохранений
- Количество загруженных Steam ID
- Результат авторизации target ID
- Прогресс brute-force (найденный ID, номер попытки)
- Создание бэкапа
- Re-sign (exit code MandarinJuice)
- Скопированные файлы
- Ошибки

## Структура репозитория

```
game-save-convert/
├── README.md          # Пользовательская документация
├── DOCS.md            # Техническая документация (этот файл)
├── install.ps1        # PowerShell-установщик (legacy)
├── steam_ids.txt      # База известных Steam ID
├── .gitignore         # Исключает *.cs, *.log, *.zip, *.mhtml
│
├── save-convert.cs    # Исходник утилиты (локально, git-ignored)
├── save-convert.exe   # Собранный бинарник (git-ignored, в releases)
├── installer.cs       # Исходник установщика (локально, git-ignored)
└── installer.exe      # Собранный установщик (git-ignored, в releases)
```
