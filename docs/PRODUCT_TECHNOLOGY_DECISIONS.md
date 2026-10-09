# Инструменты и архитектурные решения lifecycle

**Принято в Phase 7, 2026-10-09:** isolated WebGL2 projective triangle warp, shared MakeHuman topology и ортографический bounded alignment. У нового provider нет зависимости от live Three.js camera, WebGPU, сервера или внешнего inference. Source depth rejection, target z-buffer и bounded deterministic repair проходят versioned quality gates. Общий `IForecastRenderer` и structural depth/normal/mask bundle подготовлены для будущего Phase 8; AI adapter не реализован. Найденный legacy `PhotoWarp.cs`/`warp.js` сохранён и включён в сравнение. [Решение, численные limits и evidence](GEOMETRY_WARP.md).

Статус: рекомендации для [roadmap](PRODUCT_LIFECYCLE_ROADMAP.md), 2026-10-09. Код и зависимости этим документом не меняются. [Канон](PRODUCT_LIFECYCLE.md) определяет поведение; exact provider/checkpoint закрепляется только после benchmark, license, privacy и cost review.

## Сохраняемый стек и границы новых слоёв

| Область | Выбор | Существующая точка интеграции / что добавить |
|---|---|---|
| Client/domain | .NET 10, C#, Blazor WebAssembly; текущий local-first продукт | Чистые domain services в BodyModel/Core/Strength; UI orchestration без backend rewrite. |
| Current Avatar | MakeHuman assets + skeleton/atlas/fitter; Three.js/WebGL | `MakeHumanModel`, `MakeHumanRig`, `MakeHumanMuscleAtlas`, `IBodyShape.Geometry`, `ViewerInterop`; добавить revisions/provenance, сохранить procedural fallback. |
| Manual correction | Семантические controls поверх MakeHuman/mesh morph layer | `BodyForm`/morph capabilities — исходный задел; отдельный `AvatarShapeCorrectionProfile`, безопасные bounds, final mesh measurements. Не CRUD factual centimeters. |
| Current photos | Existing silhouette/photo fitting и segmentation/pose stack | `PhotoAnalyzer`, `PhotoFitter`, `SilhouetteProfiler`, `analysis.js`/MediaPipe. Нет обязательной новой большой модели. Offline recognition требует заранее доступных assets. |
| Activity | ActivityDay/ActivityEvent orchestration | `ActivityCalendar`, `StrengthJournalState/Store`, cardio journal; ссылаться на существующие payload/revisions и freeze summary, не дублировать workout domain. |
| Nutrition | Local manual model + КБЖУ/serving scaling | Новый domain calculator и coverage, без внешнего nutrition API. Future food DB/barcode adapter; saved dishes optional. |
| Forecast | Stage A composition v3 после merge/review #19 + existing calibration/training-aware shape | `ForecastEngine`, `ForecastPersonalization`, `ForecastSnapshot`, `MuscleAdaptationForecast`, `BodyShapeForecast`. Lifecycle adapter подаёт только closed behavioral evidence и factual check-ins; priors отдельно. |
| Local render | Three.js/WebGL projected mesh/depth + barycentric/dense warp | `PhotoWarp.cs`/`warp.js` — row-warp baseline; новый `GeometryWarpRenderer` развивает его, WebGPU optional. |
| AI render | Provider abstraction + managed inference с source image и structural conditioning | `ManagedDepthRenderer`, submit/status/cancel, validator; server-side secret. fal — первый benchmark candidate, не окончательный vendor lock. |
| Anatomy later | BodyParts3D fields после отдельного R&D/license/geometry gate | Не замена MakeHuman и не prerequisite для Activity/Nutrition. Следовать [R&D Stage B](FORECAST_V3_RND_PLAN.md). |
| Testing | Existing xUnit, node:test, Playwright и offline PWA smoke | New contract/state/migration/geometry fixtures в соответствующих implementation phases; docs stage не утверждает их прохождение. |

Наблюдаемая база: [BodyProfile](../src/WorkoutCalculator.BodyModel/BodyProfile.cs), [BodySnapshot](../src/WorkoutCalculator.BodyModel/History/BodySnapshot.cs), [ForecastSnapshot](../src/WorkoutCalculator.BodyModel/Forecast/ForecastSnapshot.cs), [legacy hypotheses](../src/WorkoutCalculator.Web/Services/Hypotheses.cs), [photo analyzer](../src/WorkoutCalculator.Web/Services/PhotoAnalyzer.cs), [PhotoWarp](../src/WorkoutCalculator.BodyModel/Photos/PhotoWarp.cs), [warp.js](../src/WorkoutCalculator.Web/wwwroot/js/warp.js). Новые domain types пока концептуальны; наличие похожего legacy названия не означает совпадение lifecycle.

## GeometryWarpRenderer: обязательный baseline

Текущий PhotoWarp вычисляет перемещение границ по горизонтальным срезам и warp.js деформирует строки Canvas2D; зона в основном торс, без полноценного camera-aligned dense correspondence. Сохранить этот baseline и его тесты для сравнения.

Целевой pipeline:

1. Разблокировать source photo локально; выбрать соответствующие current fitted mesh и future mesh из сохранённой endpoint geometry.
2. Зафиксировать camera intrinsics approximation/extrinsics, pose, crop, scale, segmentation/keypoints. Current и future рендерить в одинаковой системе координат.
3. Получить projected triangles/depth/visibility и current-to-future displacement. Barycentric correspondence требует совместимой topology; иначе explicit correspondence map/version, не сопоставление случайных vertex IDs.
4. Inverse texture mapping на target; depth/occlusion test, bounds и region masks. WebGL shader/Three.js — первый путь. WebGPU добавлять только при измеримой пользе и с feature detection/fallback.
5. Ограниченный локальный repair силуэта/фона; disoccluded texture, скрытая одежда и сильный поворот тела могут не восстанавливаться. Не дорисовывать мышцы ради красоты.
6. Structural validation, artifact label/version/hash. Same inputs/versions дают стабильную геометрию в опубликованной floating-point tolerance; пиксельная bit-identical гарантия между GPU не заявляется.

Inference cost = $0 внешних запросов, но время/энергия устройства не нулевые. Фото остаётся на устройстве. Scientific/structural baseline означает проверяемое преобразование geometry, не доказательство точности будущего тела и не photorealistic guarantee. При неподдерживаемых условиях — понятная причина и 3D fallback.

## Managed renderer: выбран capability, кандидат ещё проходит gate

Рекомендуемый launch path — **managed API; fal первым для capability benchmark**. Нужен source image + depth/ControlNet-like geometry control + identity/scene preservation. Generic prompt «сделай стройнее» не заменяет target mesh. FLUX/Qwen-class candidates тестируются отдельно с конкретными checkpoint/adapters и настройками.

Первичные источники проверены 2026-10-09; это snapshot capability/licensing ориентиров, не бессрочная юридическая или ценовая гарантия:

| Candidate | Подтверждённый ориентир | Решение |
|---|---|---|
| [fal FLUX General API](https://fal.ai/models/fal-ai/flux-general/api) | Schema поддерживает ControlNet/reference guidance; [страница endpoint](https://fal.ai/models/fal-ai/flux-general) указывает $0.075 за округлённый вверх megapixel и commercial use. | Первый managed structural benchmark. Проверить exact source-image/depth combination, identity fidelity, provider terms и все control weights; generic endpoint ещё не body renderer. |
| [Qwen-Image-Edit-2509 model card](https://huggingface.co/Qwen/Qwen-Image-Edit-2509) и [fal adapter schema](https://fal.ai/models/fal-ai/qwen-image-edit-2509/api) | Official model card указывает Apache 2.0 и работу с несколькими изображениями/structural conditions. | Второй кандидат. Model capability не гарантирует наличие тех же controls у конкретного hosted endpoint; benchmark exact request и adapters обязателен. Не выбирать только по названию семейства. |
| [Odo](https://github.com/FastCodeAI/Odo) | Reference/identity + depth-guided body reshaping; repository указывает CC BY-NC-SA 4.0, non-commercial research. | Только architectural/benchmark reference; code/weights не включать в коммерческий production. Лицензии vendored компонентов проверяются отдельно. |
| [RunPod Serverless](https://docs.runpod.io/serverless/overview) | Workers могут масштабироваться до нуля после idle timeout; собственный worker/container и operation responsibilities. | Later option для SelfHostedRenderer после usage gate; serverless не означает бесплатный idle/startup/storage. |

Никаких весов, API accounts, оплаченных jobs или dependency upgrades этим docs PR не создаётся. Model checkpoint, adapter weights, endpoint version/terms, разрешения на commercial use и обработку пользовательских фото заносятся в отдельный ADR перед интеграцией. Условия hosted inference и право распространять/self-host weights — разные проверки.

### Контракт providers и validator

Общий request: Hypothesis/ForecastSnapshot ID, exact target date/hash, source photo ID/consent, current revision, camera/pose alignment, structural maps+versions, view, settings, idempotency key. Provider capability flags: local/cloud, structural conditioning, deterministic, photorealistic. GeometryWarpRenderer реализует общий envelope, но не обещание photorealism в названии интерфейса.

Result: provider job/request ID, accepted/failed/cancelled state, artifact/hash, model/settings/seed (если доступен), target/source lineage, attempt count, price/currency, latency, validation report. States: Eligible → Queued → Running → Validating → Accepted либо Retry/Failed/Cancelled. Repeated callback/reload не создаёт вторую платную задачу. Внешняя модель может быть недетерминированной: replay картинки читает сохранённый artifact, а не повторяет API.

Validator сравнивает generated silhouette/widths/keypoints с проекцией **заданного** mesh; проверяет identity (consented metric/manual audit), pose, одежду/фон, руки/конечности и systematic enhancement beyond target. 2D proxy не доказывает истинные girths/BF% и не используется для physiology calibration. Thresholds/crops/estimator version фиксируются на development set до participant-held-out test; не подбирать на test set.

Benchmark: одинаковые source+target пары для local row warp, GeometryWarpRenderer, optional unconditioned image edit и managed conditioned candidates. Одинаковые ракурсы/разрешения; анализ по размерам тела, позе/одежде/occlusion и magnitude изменений; report mean/tails/failures, silhouette overlap и boundary/width error, normalized keypoint displacement, identity/scene audit, cost per **accepted** output и p95 latency. Небольшой красивый набор примеров не закрывает gate. Fixed retry budget: предлагается максимум 2 attempts/view, без изменения target; окончательный лимит/thresholds утвердить Phase 8.

## Экономика и business gate

Render — milestone: одна основная конечная точка на 14/30-day Hypothesis. 200 пользователей × 1–2 hypotheses/month = 200–400 main renders/month. Optional второй ракурс — множитель views, retries — attempts; это не ежедневные 6000 renders.

Обозначения: U — paying users; H — hypotheses/user/month; V — views/hypothesis; A — среднее число платных attempts/view; P — цена attempt при выбранном разрешении; S — месячные storage/egress/queue/validation затраты.

`C_managed = U × H × V × A × P + S`.

Пример чувствительности, **не закреплённый тариф продукта**: при указанной fal цене P=$0.075 для 1 billable MP, U=200, H=1–2, V=1, A=1 → $15–30 inference/month. V=2 и A=2 → $60–120. При 2 billable MP суммы удваиваются. Округление MP, платные failures, tax и S проверяются у exact provider; цена может измениться. Источник ставки — [fal endpoint pricing](https://fal.ai/models/fal-ai/flux-general).

Subscription revenue = U × subscriptionPrice. Если исключительно для расчётного примера цена подписки $10, revenue=$2000/month: $15–30 = 0.75–1.5%, $60–120 = 3–6% **до S и прочих расходов**. Так редкий render может оставаться малой статьёй, но ни цена подписки, ни маржа этим документом не установлены.

Для альтернативы: `C_gpu = fixed rental + active/idle/startup compute + storage/egress + operations/support + amortized setup`. Сравнивать при одинаковых accepted quality/SLO, а не цену секунды GPU с ценой успешного API изображения. Если fixed monthly cost = F, managed variable cost per accepted view = M, own variable cost = G, предварительный break-even `N = F / (M − G)` существует только при M>G и достаточной capacity; retries/validation/idle включены в M/G/F.

Предлагаемый Phase 9 decision gate: два полных месяца реальных receipts; quality/SLO не хуже managed; projected all-in alternative ≤80% managed на наблюдаемом объёме с запасом пиков; named operations owner и approved license/privacy. Этот 20% запас — предлагаемая business policy, не универсальная граница. Отдельно сравнить serverless и dedicated: аренда постоянного GPU требует подтверждённой устойчивой загрузки. До измерений результат = **managed first / no dedicated GPU at launch**.

## Backend preparation: концепция, не текущая реализация

| Future component | Ответственность | Local-first boundary |
|---|---|---|
| Auth/Profile API | Account, настройки, права, consent; separation от тела | Локальный Profile ID можно связать с account позже; signup backend не добавляется docs этапом. |
| Database | Versioned metadata, cycle/evidence/job refs, concurrency | Immutable IDs/revisions и availableAt сохраняются при sync; last-write-wins непригоден для закрытых фактов. |
| Private object storage | Photos, mesh/maps, renders, expiration/deletion | Фото загружается только при явном выборе; доступ краткоживущий/авторизованный, не публичный bucket. |
| Job/render queue | Idempotent jobs, limits/retry/cancel, provider receipts | Offline diary/3D/warp не зависят от очереди; pending job не блокирует Close Day. |
| Managed provider gateway | Secrets, request validation, consent/gate/budget checks | Ключи не передаются в browser/WASM; нет upload по одному лишь открытию Avatar. |
| Later GPU worker | SelfHostedRenderer с тем же contract | Только после Phase 9; vendor switch не меняет physiology/архив. |

Сейчас нет решения о production DB/host/queue vendor: это отдельная задача после product consolidation. Локальное хранение и backup сохраняются, cloud sync — отдельный scope с конфликтами/retention. Перед managed launch нужен минимальный безопасный gateway, не полная миграция дневника на сервер.

Не обещать бессрочную replayability удалённых по запросу пользователя фото: при явном удалении media сохраняется допустимая metadata/tombstone и объяснение unavailable artifact; numerical forecasts/immutable revision history не переписываются. Конкретный deletion/retention и разрешённые сохраняемые metadata закрепить до cloud integration.

## Что намеренно отложено

BodyParts3D muscle fields, DeltaShape/Pseudo-DXA — отдельные validated R&D stages; food dictionaries/recipes/barcodes — расширение nutrition; meditation/journal/sleep — ActivityEvent extension без UX сейчас. Автономное построение следующего дня/coach допускается только после проверки Avatar/Forecast: текущий выбор — standard template, user settings и advisory recommendations.
