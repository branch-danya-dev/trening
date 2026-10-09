# Полная резервная копия и восстановление

Откройте **Профиль → Резервная копия → Скачать полный backup**. Получится `trening-backup-v1.zip`. Для переноса откройте приложение в новом браузере и используйте «У меня есть резервная копия» на первом шаге мастера. Данные никуда не отправляются.

## Содержимое v1

| Раздел | Данные |
|---|---|
| `local.json` | Все строки localStorage с префиксом `workoutcalc.` — исходные значения без переформатирования |
| Профиль и планы | `body.v1`, `hypotheses.v1`, legacy `hypothesis.v1`, выбранный план, питание/кардио, strength program, исходные профили планов |
| Profile/Avatar domain | `avatarDomain.v1`: Profile, Avatar, все revisions, corrections/AvatarDerived/quality, frozen facts/photo refs, draft, origin/cycle events и reset gate. `body.v1/preferences.v1` после migration — сохранённые legacy before-images. |
| Журналы | `workouts.v1`, `strength.v1`, legacy `weights.v1` |
| История и прогнозы | `bodySnapshots.v1`, `forecasts.v1`, все ForecastSnapshots, calibration revisions, evidence и backup перед прежними импортами/миграциями |
| Настройки | `photoprivacy.v1`, `model.v1`, `preferences.v1`, `onboarding.v1` (включая незавершённый мастер), `validation.v1` |
| `photos.json` | Все записи IndexedDB `body3d-photos` v2: `sessions`, `images`, `settings` |
| `blobs/N.bin` | Полные фото, thumbnails, ciphertext, IV/salt/verifier — с сохранением типа Blob / ArrayBuffer / Uint8Array |

Внутренние restore-lock/epoch и recovery-журнал не являются пользовательскими данными. Статические MakeHuman/atlas/MediaPipe assets, Service Worker и caches не включаются.

`manifest.json`: `format: trening-backup`, `schemaVersion: 1`, `appVersion`, `buildVersion` (git build), `createdAt`, список `files` с именем, числом байтов и SHA-256 каждого файла. Фото-метаданные находятся в `photos.json`, версии вложенных stores сохраняются в их исходных payload. Manifest не является цифровой подписью: хеши обнаруживают повреждение, но не доказывают доверенность источника.

Формат — ZIP STORE (без сжатия), UTF-8 JSON. Поддерживается только этот формат приложения, не произвольный ZIP от стороннего архиватора. Ограничения чтения: 512 МиБ, 20 000 файлов; больших фотоархивов это может не вместить. Не переупаковывайте архив с deflate и не меняйте файлы вручную.

## Порядок restore

Avatar domain проверяется `BackupValidation` до мутаций: schema/model versions, Profile links, duplicate IDs, predecessor/origin/cycle chain, corrections, frozen facts и `AvatarDerived` source. Envelope восстанавливается byte-for-byte, включая активную revision и незавершённый draft. Старые ZIP без `avatarDomain.v1` допустимы: после restore выполняется idempotent migration. Новый формат ZIP или копии photo blobs для Avatar не создаются. Детали cutover — [AVATAR_DOMAIN](AVATAR_DOMAIN.md).

1. Выберите ZIP. До замены проверяются границы ZIP, имена/дубликаты, manifest/schema, наличие всех файлов, размеры и SHA-256, типы бинарных записей, связи фото и превью, параметры защиты PIN.
2. Профиль, cardio, BodySnapshot, strength, forecast, preferences/draft и контрольные данные проверяются теми же доменными валидаторами, что при обычном чтении. Будущая неподдерживаемая схема отклоняется.
3. Интерфейс показывает дату/сборку, количество разделов/фотосессий/файлов. Замена затрагивает **все** данные приложения, включая отсутствующие в архиве записи. Требуется отдельная галочка «Подтверждаю замену всех данных» и кнопка восстановления.
4. Под эксклюзивной browser Web Lock создаётся `trening-backup-before-restore.zip`. Проверьте, что браузер сохранил скачанный файл: автоматическое скачивание может требовать разрешения браузера.
5. До первого изменения в IndexedDB `trening-recovery` сохраняется durable before-image старых localStorage и фото. Затем заменяются localStorage и все фото-таблицы (одна IndexedDB transaction), обновляется поколение данных, очищается recovery и перезагружается приложение.
6. Исключение откатывает старые данные. Если вкладка закрылась или откат не завершился, before-image остаётся: следующий запуск возвращает прежнее состояние **до чтения stores компонентами**. Пока восстановление незавершено, новые записи заблокированы. Старые вкладки после успешного restore должны перезагрузиться и не могут переписать восстановленные данные.

Это согласованная операция над двумя механизмами хранения с журналом отката; общей браузерной транзакции между localStorage и IndexedDB не существует. Для восстановления нужно свободное место под before-image и импорт. При нехватке квоты, запрещённом IndexedDB/localStorage или отсутствии Web Locks операция не должна считаться успешной. Освободите место/разрешите хранение, закройте старые версии и перезагрузите приложение. Не очищайте данные сайта во время восстановления — это удалит и журнал отката.

## Приватность и восстановление доступа

Полный backup предназначен для владельца: он содержит даты, физические параметры, заметки и все фото. Сам ZIP целиком **не зашифрован**. Если фото были защищены PIN, их ciphertext и необходимые salt/IV/verifier сохраняются; PIN и производный ключ из памяти не записываются. В новом браузере понадобится прежний PIN. Сбросить его без потери защищённых изображений нельзя. Не передавайте полный backup для обычного анализа.

Для анализа используйте отдельный [validation-package.zip](VALIDATION.md): явный выбор разделов, без фото по умолчанию, без PIN/ключей, локальных идентификаторов и свободных заметок.

Проверки покрывают round trip всех основных stores, точное совпадение бинарных фото и настроек шифрования, restore в чистый browser context, три точки искусственного сбоя, закрытие вкладки в середине commit, защиту от устаревшей вкладки, неверный ZIP, пропавший файл, неверный хеш, будущую схему и повреждённые фото-метаданные.

## Phase 6 — Current Avatar check-ins (2026-10-09)

Full backup includes `workoutcalc.checkIns.v1` unchanged, referenced BodySnapshots, Avatar revisions, decisions for rejected observations, candidate/outcome links and all retained photo blobs/settings, including back references. Old backups without CheckIns remain valid. Strict restore checks snapshot/revision/cycle links and rejects orphan factual-update revisions. The pending CheckIn journal is recovered under the archive lock before export/restore and is never exported itself. Repeated reload cannot re-ingest a session or create another revision. Validation export blocks while a cross-store transaction is pending. Details: [commit/recovery contract](CHECKIN_LIFECYCLE.md), [exact restore evidence](evidence/checkin/README.md).
## Phase 7 — saved synthetic renders

Full backup включает отдельную `renderArtifacts` таблицу IndexedDB schema 3: `workoutcalc.renders.v1` envelope и точные output blobs либо AES-GCM ciphertext/IV/type вместе с прежними PIN settings. Нет повторного encode или regeneration при restore. Depth/normal buffers transient и не сохраняются. Старый backup без этой таблицы допустим и восстанавливается с пустыми renders.

Preflight проверяет synthetic=true, provider/version, hashes, frozen Hypothesis/core/revision/endpoint/source links, quality policy и byte checksum (для ciphertext — при decrypt). C# domain validation выполняется до restore mutation. Полная metadata/bytes входят в recovery before-image и общий Web Lock/generation guard. Source-delete атомарно удаляет derived renders; удаление одного render не удаляет Hypothesis. При восстановлении всего backup возвращается исходный согласованный source+render набор. Скачать image вне приложения — отдельный файл вне cascade.

Saved images не исключаются из «полного» backup. Лимиты ZIP остаются прежними; размер зависит от количества PNG и исходных фото. [Exact encrypted round-trip и measured fixture size](evidence/geometry-warp/README.md), [storage contract](GEOMETRY_WARP.md).
