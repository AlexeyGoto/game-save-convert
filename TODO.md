# Game Save Convert — TODO / Roadmap

## v4.0 (текущий релиз) — offline-first, быстрый brute-force, без whitelist

- [x] Chunk-based parallel brute-force (~830M/поток, ~5.9B/все ядра, worst case 1-17 сек)
- [x] Гибридный режим: download steam_ids.txt (1 сек) → list search → brute-force
- [x] Offline-first: без интернета → сразу brute-force (не exit 1)
- [x] Убрана проверка target ID по whitelist (exit 3 удалён)
- [x] Удалён IdReporter (отправка ID на сервер)
- [x] Удалён known_ids.txt (локальный кэш бесполезен на замороженных ПК)
- [x] Удалена команда check
- [x] remotecache.vdf: read-only атрибут для защиты от Steam Cloud
- [x] Динамический InstallDir (не привязан к C:\Tools\SaveCompat)
- [x] Installer v4: без consent panel, кнопка сразу активна
- [x] Benchmark команда
- [x] Обновление README.md, DOCS.md, TODO.md

## v3.0 — RE9 Steam <-> Crack

- [x] Перенос сохранений Steam <-> Crack (re-encrypt)
- [x] Автодетекция платформы (Steam/GSE по пути)
- [x] Даунгрейд BUILD при переносе на crack (0x01001002 → 0x01001000)
- [x] Обработка data00-1.bin: BUILD patch + version counter +2
- [x] Генерация remotecache.vdf для Steam Cloud Sync
- [x] Валидация BUILD — блокировка неподдерживаемых версий
- [x] Флаги -crack / -steam для принудительного выбора платформы

## Известные ограничения

- **Steam Cloud**: при переносе на Steam необходимо вручную отключать облако (remotecache.vdf read-only помогает, но не гарантирует)
- **Первые сохранения**: на Steam-аккаунте должны быть хоть какие-то сейвы (дойти до первого чекпоинта)
- **Перезагрузка после установки**: PATH обновляется только после reboot

## Прочее (backlog)

- [ ] Поддержка нескольких папок сохранений за один запуск
- [ ] Проверка целостности после конвертации (decrypt target → success)
- [ ] Ремонт сейвов с разной шифровкой (data00-1 под одним ID, остальные под другим)
