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

## Phase 2 — создание, коррекция и фиксация (2026-10-09)

Реализовано от `main 932a2aa897ed561fb94375f93705a614d2b2aa7f` после обычного merge #21. [CI этой базы](https://github.com/branch-danya-dev/trening/actions/runs/37879897119) прошёл. Описания Phase 1 выше фиксируют прежнюю версию. Ниже — актуальный контракт Phase 2.

### Один мастер и устойчивое сохранение

Существующий `Onboarding` расширен до восьми шагов: системные поля → необязательные обхваты → известный BF% → необязательные фото → цель → подтверждение фактов → реконструкция/коррекция → обзор/Lock. Незавершённый системный профиль сохраняется в том же onboarding draft; Profile/Avatar domain envelope создаётся атомарно после подтверждения исходных фактов. Независимого второго мастера нет.

Первый BodySnapshot содержит только подтверждённые поля. Его стабильный ID сохраняется до записи; после прерывания записи создание возобновляется с тем же ID. Пустой BF%, обхваты, осанка и форма остаются пустыми в BodySnapshot. После появления domain draft возврат к произвольному переписыванию первого факта закрыт: factual changes идут через историю замеров. Перезагрузка на коррекции/обзоре и закрытие вкладки восстанавливают тот же draft; повторный confirm не создаёт вторую ревизию. После прерывания между domain Lock и завершением мастера загрузка завершает только marker onboarding.

`Зафиксировать аватар` требует просмотра и подтверждения источников/предупреждений. Domain API повторно строит конечную геометрию; только успешный CAS активирует immutable revision/origin/cycle. Model показывает дату, качество, источники и отдельное действие `Исправить аватар`. Оно объясняет новый отсчёт, открывает отдельный draft и переводит на Model для 3D preview. Старые ревизии/forecasts не переписываются. В новой сессии можно использовать факт из BodySnapshotStore или перейти к записи нового замера. После confirm reset gate закрывается явной кнопкой `Начать новый цикл прогнозов`.

### Семантическая геометрия v2

`avatar-builder-2` + `shape-corrections-2` используют прежнюю MakeHuman topology, skeleton, atlas и fitter. Все новые creation/recalibration sessions используют v2. `avatar-builder-1`/`shape-corrections-1` остаются доступными для точного replay старых revisions; новые поля имеют пустые defaults. При открытии новой recalibration старые значения перенесены в v2, чей preview проверяется пользователем перед новой фиксацией. Старый архив не пересчитывается новой семантикой.

| Поле | UI | Диапазон и действие |
|---|---|---|
| ShoulderWaistShape | Ширина плеч | −1…1, боковое смещение верхнего корпуса/плеч |
| ChestFullness | Выраженность груди | −1…1, локальная форма передней груди |
| AbdomenProminence | Выступ живота | −1…1, передняя проекция живота |
| FlankFullness | Бока | −1…1, боковая проекция живота с плавным переходом |
| WaistFullness | Форма талии | −1…1, ширина корпуса в области живота |
| GluteShape | Форма ягодиц | −1…1, задняя проекция таза |
| ArmFullness | Форма рук | −1…1, передняя/задняя форма верхней руки |
| LegFullness | Форма бёдер и ног | −1…1, форма верхних бёдер, не длина ног |
| TorsoDepth | Глубина корпуса | −1…1, передняя/задняя проекция корпуса |
| PostureOffset.Kyphosis | Сутулость | −15…25°, существующий posture rig |

Ноль — identity, шаг формы 0.1; доступны числовой ввод, клавиатура, reset одного/all. Shape fields направлены по существующим skin zones, сглажены smoothstep, ограничены ±30 мм поперёк и ±35 мм в глубину при 175 см; это инженерные bounds, не антропометрические истины. Защищённые head/neck/hand/foot zones возвращаются к posed prior с плавным переходом. Осанка может перемещать голову/кисти целиком через прежний rig, но не деформирует их отдельными shape controls.

Factual и PhotoDerived girths остаются неизменными targets solver. Только не подтверждённые girth priors могут меняться до ±8% в отдельном geometry DTO. Факты, BF% и numerical forecast formulas не меняются. Существующий fitter reconciles обхваты после применения shape layer; затем измеряется итоговая сетка. При известном residual > **0.5 см** review показывает конкретный обхват, signed residual и ограничение. Missing sections и предел ткани также видимы. Для несовместимых исходных данных гарантии отсутствия residual нет; silent break запрещён, пользователь явно подтверждает предупреждения. Controls при фиксированном обхвате перераспределяют форму, а не обещают произвольное увеличение окружности.

Derived: восемь mesh girths, shoulder-joint-span/waist, waist/hip, объём mesh как proxy, breadth/depth chest/waist по надёжно найденным tape sections, разница girths/volume до и после corrections, RMS/max смещения вершин. Всё `AvatarDerived`; BF%/muscle mass из сетки не вычисляются. Дополнительные metrics рассчитаны **после** окончательной геометрии.

### Фото и качество

Existing front/side local segmentation/silhouette/PhotoGirths pipeline подключён кнопкой `Уточнить черновик по фото` в исходном мастере. Поддерживаемые estimates — талия и бёдра. Ручные обхваты всегда имеют приоритет; фото с уже измеренными обхватами сохраняется как ссылка для визуальной проверки. `AvatarReconstructionInputs.PhotoEstimates` хранит cm, RMSE и session reference отдельно от `Fact`; frozen BaseProfile допускает эти значения только с PhotoDerived provenance. Фото не создаёт второй starting snapshot и не подставляет скопированный из metadata вес/BF% в факты.

Quality gate `front-side-avatar-policy-1`: оба ракурса, корректный масштаб, ≥30 пригодных уровней каждого силуэта, видимость shoulders/hips pose ≥0.8, отсутствие предупреждений и совпадение sex/height с draft. Accepted reference хранит 0.8 как **policy sentinel**, не индивидуальную вероятность точности. Низкое качество/один ракурс/предупреждения сохраняют прежние inputs и явно объясняются. Метаданные и blobs остаются в IndexedDB; новых upload/backend зависимостей нет. Back не предлагается: existing pipeline не поддерживает этот ракурс. Автоматический check-in после lock не добавлен.

`AvatarQualitySummary` — policy `avatar-quality-1`: количество factual/profile, photo-derived, estimated полей, принятых photo sessions, magnitude corrections, fitter/known-girth residuals и warnings. `Базовая модель` — менее 3 ручных обхватов без photo-derived полей; `Уточнённая` — ≥3 ручных или PhotoDerived; `Хорошо подтверждена` — ≥6 ручных + accepted photo, все известные residuals ≤0.5 см, нет missing sections/soft-tissue limit. Это категории покрытия, не Accuracy %. Независимая проверка сходства на людях остаётся необходимой.

### Производительность, backup и границы

События slider сразу сохраняют draft; перестройка debounced 180 мс, виден loading state, confirm недоступен до готовности. MakeHuman/atlas не перезагружаются. Холодный исходный fit кэшируется; каждый corrected solve начинается от **того же** prior, а не от предыдущего положения ползунка. Поэтому warm cache не создаёт зависимости от порядка действий. Диагностика отдельно измеряет реальное время AvatarBuilder, а не только передачу уже готовой сетки.

Full backup сохраняет все domain revisions, текущий draft, corrections/derived metrics, onboarding step/selected photo и исходные photo blobs. Резервная копия доступна и внутри создания аватара. Restore не меняет цепочку revisions или locked state. `Avatar Validation` выводит source/girth residuals и pre/post deltas; similarity 1–5 хранится только в validation store. Export opt-in, pseudonymized, allowlisted; фотографии по умолчанию отсутствуют.

Отложены muscularity/softness control (чтобы не смешивать с BF%), независимая эмпирическая accuracy calibration, асимметрия, новые photo-fit/posture inference controls, back pipeline, ActivityDay, питание, Hypothesis lifecycle, check-in progression, GeometryWarpRenderer, managed render, BodyParts3D и DeltaShape. #19 formulas/benchmark остаются отдельными.

### Проверенный результат Phase 2

Локально: 420/420 .NET, 46/46 JS; Release build 0 warnings / 0 errors. Все десять browser suites прошли: skeletal, strength, history, forecast, muscle-forecast, product, avatar, avatar-creation, errors, published offline `/trening/`. `avatar-creation` включает clean restore как locked state, так и незавершённого draft. 320/390/1400 px без горизонтального overflow, browser errors = 0.

[Browser evidence и timings](AVATAR_CREATION_SMOKE.json): Windows, Release WASM, headless Edge/SwiftShader. Cold identity builder 150.3 мс; corrected warm updates: shoulders 199.4, abdomen 220.9, flanks 225.6 мс. Дополнительно 180 мс debounce перед rebuild. Это desktop CPU при mobile viewport, не физический телефон; на слабом устройстве rebuild может быть медленнее. Повторные assets не загружаются. Warm означает cached deterministic prior, не предыдущую исправленную сетку. Нулевая коррекция содержит меньше работы, поэтому cold identity нельзя трактовать как сравнение скорости того же corrected input.

PR #19 (`62ad4811169d8ea8c3de22247c4ce0712b9c7763`) не смержен и его formulas/benchmark не перенесены. Проверка combined tree `60fa9891e8e28e81ea0368972cec662cae16c3c1` от implementation commit `e134e8b` прошла без конфликтов; изолированная combined Release сборка — 0 warnings/errors и 457/457 .NET. Документация, CSS и validation additions расположены отдельно от областей #19. После проверки изменены только эти сведения о тестировании. Identity path валидирует inputs/corrections; replay и restore отклоняют смешанные версии reconstruction/correction.
