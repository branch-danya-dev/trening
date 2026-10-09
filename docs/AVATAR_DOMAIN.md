# Avatar domain — Phase 1

Реализация от `main 9878994f4a0766f8ea8599c3fbb2b12e355a2701`, после обычного merge документационного #20. #19 (`62ad481…`, Forecast v3 composition Stage A) остаётся отдельным открытым PR. Его формулы, benchmark и versioned calibration не перенесены.

## Сущности и ownership

| Сущность | Реализация / назначение |
|---|---|
| `Profile` | Системный ID, CreatedAt, Sex, HeightCm, BirthDate либо AgeAtCreation с датой CreatedAt, Goal, ActiveAvatarId, schema. Настройки пульса/VO2 для калькулятора; нет веса, обхватов, mesh или body history. |
| `AvatarState` | ID/ProfileId, CreationSource, CreatedAt, Status, ActiveRevisionId, TrackingOriginRevisionId, TrackingCycleId. Immutable collections revisions/events и optional draft. |
| `AvatarRevision` | ID/AvatarId, CreatedAt/EffectiveDate, Source, predecessor, frozen reconstruction inputs, correction layer, final mesh metrics/quality, builder/fitter/asset versions, confidence/reason. |
| `AvatarReconstructionInputs` | Frozen base BodyProfile JSON, копия конкретного BodySnapshot (включая исходный ID), происхождение каждого поля, photo session/pipeline/confidence metadata. Сериализованный DTO разворачивается в новый объект. |
| `AvatarShapeCorrectionProfile` | Безразмерные bounded controls, posture offset; `shape-corrections-1`. Входные замеры не меняет. |
| `AvatarDerivedMetrics` | Только `Source=AvatarDerived`; значение/единица/modelVersion/confidence. |
| `AvatarDraft` | Стабильный ID, predecessor, CreatedAt, frozen inputs, corrections, reason. Первичный или recalibration draft переживает reload. |
| `AvatarCycleChanged` | Durable событие с прошлым и новым origin и новым cycle ID. Основа для будущего Hypothesis lifecycle. |

Ревизии deeply immutable: records, immutable arrays/dictionaries, строка frozen adapter. Текущий статус ревизии определяется указателем `ActiveRevisionId`; предыдущие элементы считаются архивными без изменения их payload. Список нескольких Avatar поддерживается схемой, но UI и команды v1 создают один active Avatar; управления дополнительными аватарами пока нет.

## State machine и API

```text
Create → Draft → EditDraft → Confirm → Active (locked)
Active → StartRecalibration(reason) → Recalibrating + isolated draft
Recalibrating → EditDraft → Confirm → Active + new revision/origin/cycle
Recalibrating → CancelRecalibration → прежний Active без изменения origin
Active → ApplyPhotoCheckIn → Active + new revision, прежний origin/cycle
```

`AvatarLifecycle.EditDraft` отказывает без draft, даже при прямом вызове вне UI. Повторное открытие сессии, confirm без draft и photo update во время recalibration отклоняются. `AvatarDomainStore` предоставляет только команды: произвольного `Save(revisions)` нет. Все persistent команды сравнивают ранее прочитанный payload (CAS). Повторный confirm после успешного commit не создаёт ревизию; повторная миграция не пишет; photo check-in с уже использованным BodySnapshot ID не применяется.

После manual confirm `HypothesisResetRequired=true`: новые ForecastSnapshots нельзя сохранить до отдельного подтверждения «Начать новый цикл прогнозов». Это сбрасывает только gate и выбирает новый preview. Старые forecasts остаются доступными и неизменными. Legacy `StoredHypotheses` остаются сценариями планирования; они не объявляются новой сущностью Hypothesis и не получают фиктивный lifecycle. Photo check-in сохраняет origin и не открывает новый cycle.

`Archived` зарезервирован для будущего управления дополнительными Avatar; UI/команды архивации агрегата в Phase 1 не добавлены. История ревизий уже доступна.

## Построение и коррекция

`AvatarBuilder.Capture(Profile, BodySnapshot?, legacy?, photoReferences?)` замораживает base и происхождение полей. Известные значения совпадают с исходным фактом; пробелы остаются `VisualEstimate`, legacy-only тело — `LegacyVisualEstimate`. Валидатор запрещает противоречие base фактам и повышение estimates до factual. Старые BodyForm/Posture сохраняют существующую семантику источника; новая visual correction хранится отдельно.

`Build(inputs, corrections)` создаёт отдельный geometry DTO, выполняет **cold** MakeHuman fit, затем измеряет готовое тело. MakeHuman/skeleton/atlas/fitter не переписаны. `Rebuild(revision)` требует поддерживаемые builder/fitter/asset versions. Identity correction совпадает с прямым cold MakeHuman build по всем vertex positions. При том же input/version результат детерминирован; warm fit не служит источником сохранённой ревизии. Активный 3D использует этот результат; старые снимки истории продолжают свой прежний factual playback.

| Control | Диапазон | Действие |
|---|---|---|
| ShoulderWaistShape | −1…1 | Существующий VShape target |
| AbdomenProminence | −1…1 | Stomach target |
| GluteShape | −1…1 | Buttocks target |
| TorsoDepth | −1…1 | TorsoDepth target |
| Chest/Waist/Arm/LegFullness | −1…1 | До ±8% соответствующего girth target **в отдельном geometry DTO** |
| PostureOffset | Пределы `Posture` | Смещение относительно frozen base, с ограничением суммы существующими пределами |

Derived v1: восемь доступных mesh girths, waist/hip, shoulder-joint-span/waist, mesh volume в литрах. Отношение плеч использует span суставных landmarks на финальном теле, не окружность плеч. Не найденное сечение не подменяется input DTO: метрика отсутствует, Girth входит в quality.MissingGirths. Quality также содержит достижение лимита мягких тканей и максимальный fitter residual. Не конечная сетка или недопустимый объём отклоняются до commit.

`Confidence=null` у geometry metrics означает отсутствие валидированной оценки точности; это не 100%. BF%, leanness/training-status и regional volume proxies пока не вычисляются. Из производных данных не делаются медицинские выводы. В forecast formulas не передаются corrections/derived values: текущие числовые формулы и baseline calibration сохранены.

## Automatic photo path

`AvatarDomainStore.PhotoCheckIn(lifecycle, frozenInputs, now, effectiveDate)` — отдельный сервисный путь без ручного confirmation. Требует Photo BodySnapshot, metadata session/pipeline и confidence у observation и всех фото ≥ **0.8**. Порог — консервативная policy v1, не клинически/эмпирически откалиброванная вероятность. Пустая provenance, низкая уверенность, несовпадающая/устаревшая дата и повторный check-in не заменяют revision. Corrections последней активной ревизии переносятся; новая provenance фиксируется; origin остаётся прежним.

Подключение существующего photo UI к этому API отложено до Phase 2/6: пока старый callback фото не может незаметно переписать active shape. Фото сохраняются прежним store. Наличие API не означает, что реальная точность фото уже проверена.

## Storage, migration и восстановление

Новый versioned key: `workoutcalc.avatarDomain.v1`, schema 1. Содержит **раздельные collections Profiles и Avatars в одном atomic envelope**. Это осознанное решение: два независимых localStorage keys не обеспечили бы атомарности Profile.ActiveAvatarId ↔ Avatar.ProfileId ↔ revision/cycle. Внутри нет второй writable копии одних данных.

Reads никогда не пишут. После загрузки MakeHuman, если домен отсутствует и onboarding завершён, приложение вызывает явную команду migration `legacy-body-v1-to-avatar-1`. До записи строго читаются и валидируются legacy profile/preferences и body history. Берётся последний факт не из будущего; без фактов legacy форма остаётся estimate. Создаются один Profile, один locked Avatar, одна Migration revision и origin event. Одна CAS-запись — единственная commit boundary. Quota/read/CAS failure сохраняет всё прежнее; повтор после reload безопасен. На новом onboarding создаётся Draft, confirm остаётся отдельным действием.

`body.v1`, `preferences.v1`, BodySnapshot IDs/payloads, photo metadata/blobs, weights, legacy forecast origins не удаляются и не переписываются миграцией. `body.v1/preferences.v1` после cutover — оставленные before-images для совместимости; UI читает системные настройки нового Profile и не сохраняет туда новые формы/настройки. BodyProfile остаётся DTO для калькулятора, history playback и forecast. Старый клиент не понимает новых ревизий: для rollback использовать полный backup/совместимый reader, не запускать старый shape writer как источник новой истины.

Будущая schema/version, повреждённый payload, duplicate IDs, broken predecessor/origin/profile links блокируют запись. BrowserJournalStorage применяет общие restore lock/generation guards. Сохранённые facts могут позже редактироваться/удаляться в своей истории; frozen copy внутри revision остаётся доступной для replay и не является вторым редактируемым фактом.

Full ZIP уже включает все `workoutcalc.*` keys; новый домен входит автоматически и проверяется `BackupValidation` до любых изменений. Restore сохраняет исходную строку envelope byte-for-byte. Процедура recovery/rollback из #18 не меняется. Старые ZIP без нового key допустимы: после восстановления выполняется миграция. Validation package добавляет отдельный opt-in раздел с `avatar-N`/`revision-N`, allowlisted corrections/quality/derived values и опциональной связью `fact-N`; исключает local IDs, reason/notes, raw reconstruction JSON, photo session IDs, PIN и ключи.

## Forecast compatibility и #19

`ForecastSnapshot.AvatarOrigin` — optional metadata с AvatarRevisionId, TrackingOriginRevisionId, TrackingCycleId и версиями. `AvatarForecastOrigin.Attach` вызывается только для нового сохранения после подтверждения/gate; запрещает future revision. Старые snapshots без поля load/replay, их points не пересчитываются. Новые facts/calibration и recalibration не переписывают архив.

Интеграция ограничена metadata свойством и adapter при `SavePreview`; `ForecastEngine`, composition parameters и calibration algorithms не менялись. PR #19 остаётся prerequisite будущей Phase 5, но не этой фазы. При его слиянии сохранить оба независимых изменения; проверка merge-tree и фактическая test matrix фиксируются в PR.

## Границы Phase 1

Минимальные controls и history доступны через Model advanced section и Profile. Полноценный onboarding/review accuracy UX, независимые flank/muscularity targets, эмпирическая confidence, выбор нескольких Avatar, photo UI wiring и полноценный Hypothesis archival/ActivityDay/nutrition/rendering не реализованы. Исторические fitter binaries не пакуются: неизвестная будущему reader версия должна вызвать явный отказ, а не пересчёт чужой версией. При будущей смене fitter/asset обновить version и сохранить совместимый replay implementation.

Проверки: `AvatarDomainTests`, `avatar-backup.test.mjs`, `avatar-smoke.cjs`; общий CI также запускает прежние product/forecast/history/muscle/strength/skeletal/errors/offline suites. Ручная точность тела проверяется отдельным протоколом в [VALIDATION](VALIDATION.md), а не объявляется доказанной по smoke.

Локальный итог 2026-10-09: **410/410 .NET**, **45/45 JS**, Release build **0 warnings / 0 errors**. Девять browser suites прошли: skeletal, strength, history, forecast, muscle-forecast, product, avatar, errors, published offline `/trening/`; browser errors = 0. Проверены 320/390 px, draft reload, новый mesh, stale-tab CAS, старый forecast replay, opt-in export и точный restore аватарной истории. Основной product smoke также проверяет восстановление в чистом контексте, encrypted photos и rollback/recovery. CI основной базы `9878994f` зелёный; Pages deploy базы по-прежнему отвечает 404 при создании deployment (настройки hosting не менялись).
