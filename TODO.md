# Game Save Convert — TODO / Roadmap

## v5.0 (текущий релиз) — универсальный конвертер RE Engine

- [x] Поддержка всех 8 RE Engine игр (RE9, DD2, MHW, MHS3, DR, KG, PRAGMATA, MMSF)
- [x] Авто-детекция игры по AppID из пути к сохранениям
- [x] Параметр -game стал опциональным
- [x] Расширенные алиасы для всех игр
- [x] BUILD patching gating: даунгрейд только для RE9 (или при явном -targetsavebuild)
- [x] Silent mode graceful: неизвестный BUILD → re-sign без ошибки
- [x] pre-launch-steam.cmd: все 8 игр + авто-детекция через %SteamAppId%
- [x] Installer: URL профилей обновлён на latest
- [x] Динамический AppID в RemoteCacheGenerator
- [x] Обновление README.md, README_EN.md, DOCS.md

## v4.0-4.3 — offline-first, быстрый brute-force, без whitelist

- [x] Chunk-based parallel brute-force (~830M/поток, ~5.9B/все ядра, worst case 1-17 сек)
- [x] Гибридный режим: download steam_ids.txt (1 сек) → list search → brute-force
- [x] Offline-first: без интернета → сразу brute-force (не exit 1)
- [x] Убрана проверка target ID по whitelist (exit 3 удалён)
- [x] Удалён IdReporter (отправка ID на сервер)
- [x] remotecache.vdf: read-only атрибут для защиты от Steam Cloud
- [x] Динамический InstallDir (не привязан к C:\Tools\SaveCompat)
- [x] Installer v4: без consent panel, кнопка сразу активна
- [x] Benchmark команда
- [x] Автоматический даунгрейд BUILD (v4.3)
- [x] -targetsavebuild override (v4.3)

## v3.0 — RE9 Steam <-> Crack

- [x] Перенос сохранений Steam <-> Crack (re-encrypt)
- [x] Автодетекция платформы (Steam/GSE по пути)
- [x] Даунгрейд BUILD при переносе на crack
- [x] Обработка data00-1.bin: BUILD patch + version counter +2
- [x] Генерация remotecache.vdf для Steam Cloud Sync

## Известные ограничения

- **Steam Cloud**: при переносе на Steam необходимо вручную отключать облако (remotecache.vdf read-only помогает, но не гарантирует)
- **Первые сохранения**: на Steam-аккаунте должны быть хоть какие-то сейвы (дойти до первого чекпоинта)
- **Перезагрузка после установки**: PATH обновляется только после reboot
- **BUILD patching**: работает только для RE9 (смещения/константы специфичны)

## Прочее (backlog)

- [ ] Поддержка нескольких папок сохранений за один запуск
- [ ] Проверка целостности после конвертации (decrypt target → success)
- [ ] Ремонт сейвов с разной шифровкой (data00-1 под одним ID, остальные под другим)
- [ ] Автоматическое определение BUILD-смещений для других игр
