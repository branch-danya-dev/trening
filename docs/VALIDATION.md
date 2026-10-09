# Ручная проверка продукта

Цель — проверить понятность приложения и согласованность фактов/оценок/прогнозов на собственных наблюдениях. Функциональные smoke-тесты не доказывают физиологическую точность модели. Полный backup сделайте до эксперимента; исходные измерения храните отдельно.

## Manual Avatar validation — Phase 1

Автоматические gates домена/хранения не доказывают визуальную точность фигуры. Выполнять протокол на добровольно предоставленных данных, сохранять исходные факты и `AvatarDerived` раздельно. Схема/API — [AVATAR_DOMAIN](AVATAR_DOMAIN.md).

1. До проверки скачать полный backup. Записать build, builder/fitter/asset/correction versions, текущую revision, source/effective date и missing input fields. Отдельно указать, какие значения измерены лентой, получены из фото или заполнены visual prior. Не заменять неизвестный BF% производной оценкой.
2. Создать factual BodySnapshot с независимыми обхватами на прежних стандартных уровнях. Сравнить с final mesh girths в «Профиль → Ручная проверка»: по каждому уровню сохранить factual cm, mesh cm, signed/absolute residual. Отсутствующее сечение отметить отсутствующим; не подставлять DTO. Сопоставить quality.MissingGirths, MaximumGirthResidualCm и SoftTissueLimitReached.
3. Сделать визуальный аудит текущего аватара спереди/сбоку/сзади при нейтральной позе и одинаковой камере: плечи, грудь, живот, талия, ягодицы, руки/ноги, осанка, пересечения и неестественные деформации. Зафиксировать реальные фото отдельно от рендера; никакого AI-image evidence.
4. Нажать «Исправить аватар». Сохранить before screenshot и baseline revision. Менять по одному bounded control; записать его dimensionless delta, after screenshot, изменения mesh girths/ratios/volume. Factual BodySnapshot и frozen base старой revision должны остаться byte/semantic неизменными. ±8% fullness target — характеристика geometry adapter, не поправка factual сантиметров.
5. Проверить cancel: прежний origin/mesh/history сохранены. Повторить с confirm: появился новый predecessor-linked revision, active pointer сменился, старая revision осталась, новый цикл требует явного подтверждения. Reload должен восстановить те же controls, revision и mesh. Попытка изменить active shape через обычный flow недоступна.
6. Отдельно проверить photo API на синтетических/разрешённых данных: confidence ниже 0.8, отсутствие metadata или stale date сохраняют текущую revision; допустимый check-in создаёт PhotoCheckIn revision с provenance и прежним tracking origin. Это policy gate, не доказательство качества фото. Реальный check-in UI подключается позже.
7. Сохранить прогноз до recalibration и после явно начатого нового цикла. Проверить исходный AvatarOrigin и отсутствие изменений прежних numeric points. Legacy snapshot без AvatarOrigin должен проигрываться как раньше.
8. Backup → restore в чистом браузере: active revision, вся history, draft/events, corrections/metrics и фото должны совпасть. В другой открытой вкладке запись после restore должна блокироваться. Future-schema archive должен отклоняться до замены данных.
9. В validation package выбрать «Ревизии аватара и производные метрики». Проверить `avatar-N`/`revision-N`, `Source=AvatarDerived`, units/modelVersion/null confidence, corrections, quality и optional `fact-N`; отсутствие исходных IDs, photo session IDs, notes/reason, raw reconstruction JSON, PIN/keys. Фото включаются отдельным явным выбором.

Итог ручного отчёта: профиль/ракурс/контроль, before/after, объективные residuals, визуальные замечания, версия/качество и ограничения. Не выдавать training-status/BF% за измерения: таких metrics в Phase 1 нет. Независимая оценка accuracy на людях остаётся задачей Phase 2.

## Validation целевого lifecycle — критерии будущих фаз

Канон: [PRODUCT_LIFECYCLE](PRODUCT_LIFECYCLE.md); задачи: [roadmap](PRODUCT_LIFECYCLE_ROADMAP.md). Матрица этого раздела **ещё не является выполненными тестами или доступным UI**. Нижележащий протокол из 14 шагов, числа тестов и performance описывают базу #18. Stage A #19 проверяется отдельно и не закрывает lifecycle/renderer gates. При внедрении каждой фазы добавить фактический report: main/head SHA, versions, dataset/fixtures, команды, browser matrix, failures и GO/NO-GO.

| Проверка | Процедура / независимая опора | Acceptance / NO-GO |
|---|---|---|
| Current Avatar review | На одинаковых позе/масштабе сравнить measurements-only, +photos и +manual corrections; tape/референсные фото держать отдельно от inputs для оценки | Обязательный просмотр и объяснение confidence до Confirm; субъективное «похож» не объявлять доказанной accuracy. |
| Correction integrity | Зафиксировать measured fields/hash, изменить плечи/живот/бока/грудь/ягодицы/руки/ноги/визуальную полноту, Confirm/reload | Все factual поля неизменны; рабочая mesh меняется, derived values имеют AvatarDerived/source revision. NaN/self-intersection/недопустимый residual — reject/fallback. |
| Correction accuracy | Измерить before/after silhouette/landmark/mesh-girth residual и повторяемость коррекции; held-out участники и разные формы/одежда | Публиковать ошибки/ухудшения и subgroup results. Улучшение субъективной likeness не валидирует BF%/muscle mass/training status proxy. |
| Lock/recalibration | Проверить обычные экраны, отдельную session/cancel/confirm, старый forecast после нового origin | Нет свободных shape sliders; cancel ничего не меняет; commit атомарно архивирует cycle/hypothesis и создаёт revision+origin. Старые hashes/replay сохранены. |
| Automatic factual update | Good/low-confidence/partial/photo-only check-in; повтор и запоздалое завершение fit; reload | Good → новая current revision с provenance, origin прежний; bad → uncertainty/last-good fallback. Photo-only не фабрикует measured values ради schema. |
| Plan/fact boundary | Создать незакрытые plan/events, изменить шаблон, вызвать forecast; затем review/Close Day | Draft/Planned/Open дают ноль behavioral evidence. Template edit не меняет closed plan; после Confirm используется frozen ClosedActivityDay. |
| Close Day integrity | Duplicate confirm, stale tab, interrupted writes, amendments underlying strength/cardio/nutrition | Один commit на idempotency key; partial failure не публикует факт. Amendment создаёт superseding revision, старые Hypothesis видят старый payload. |
| Continuous calendar | Пропуск дней, часового пояса/полуночи, три варианта ответа о вчера, RestDay с едой/ходьбой | Даты не исчезают; MissingData не RestDay; unknown не 0; закрытая localDate не сдвигается. Gaps увеличивают uncertainty. |
| Nutrition v1 | Standard350/actual175; basis100; decimal/unknown/zero/negative/NaN; несколько meals и согласованность kcal/macros | Каждый показатель масштабируется ×0.5 в примере; sum без промежуточного округления. Ошибки до Confirm; total energy balance не путается с calories workout. |
| Meal timing | Повторить прогноз с теми же totals/evidence и изменённым только временем завтрака | Нет body composition штрафа/food-quality score; concrete metrics и coverage сохраняются. |
| Maturity/evidence | 0/2/3/6/7 closed days, incomplete RestDays, gaps/старые записи, 3–5/6–8+ недель с/без check-ins | Preliminary с 3+ дней при policy допускается, confidence низкий; count не заменяет coverage. 0–2 блокирует AI, но допустимые numeric/3D scenarios остаются. |
| Exact horizons/origins | 14 и 30 calendar days, граница месяца; новый Close Day после Save; поздно внесённый старый факт | 30-дневный endpoint не 28-дневный; frozen origin не меняется. No look-ahead по recordedAt/availability, не только observed date. |
| Check-in и outcome | Weight-only/partial/photo-only/missing/late outcomes, matching window/offset, reset до target | Только известные поля Forecast vs Fact; ExpiredWithoutOutcome без accuracy/calibration. Manual reset не physiological outcome. Поздняя evaluation — новая revision. |
| Calibration/replay | Изолировать версии composition/shape/cycle; затем добавить outcome и повторно открыть прошлый прогноз | Calibration меняет только будущие origins. Derived/render не truth labels; old numeric points/meshes не пересчитываются новым engine. |
| Local geometry warp | Same mesh identity, positive/negative deltas, front/side, occlusion, WebGL loss/WebGPU unavailable; сравнить row warp и full-mesh warp | Geometry/hash/tolerance воспроизводимы, нет upload и paid inference; unsafe warp → 3D, не photorealistic promise. |
| AI eligibility | Каждый gate отдельно false/unknown: unsaved Hypothesis, mutable/missing target, <3 дней/coverage, bad Avatar, unsuitable source, no consent | Ноль paid calls при fail; UI сообщает конкретную причину; допустимый 3D forecast доступен. |
| AI structural adherence | Fixed participant-held-out source+target pairs: local warp, unconditioned baseline, conditioned renderer | Silhouette/boundary/width error, normalized keypoint displacement, identity/scene audit, no extra enhancement. Thresholds фиксируются до test set; fail → bounded retry или reject. |
| Synthetic separation | Попытаться использовать generated artifact как photo check-in/calibration source; проследить полный data lineage | Render никогда не становится BodySnapshot/evidence. «Красивее» не основание менять target mesh. |
| Cost/privacy/reliability | Consent/reject/cancel/retry/timeout/reload; второй ракурс; receipts и delete | Нет secrets в browser или implicit upload; учитываются paid failures, caps/idempotency; provider выключается независимо от дневника/3D. |

### Протокол независимой проверки формы и прогноза

До пользовательского claim заранее определить dataset consent/допустимое использование, целевые subgroup, held-out participants, measurement protocol, error metrics и threshold policy/version. Визуальные корректировки могут вносить subjective bias; независимый reference не должен быть тем же mesh/photo estimator, который создавал вход. Нельзя отбирать только удачные тела или картинки.

Current Avatar: отдельно оценивать fit к ручным обхватам, silhouette/landmark residual, before/after ручной коррекции и её repeatability; фиксировать источник ошибок, одежду, позу, ракурс и confidence. BF%/training status/muscularity proxies допускаются как research priors до независимой validation; не выдавать mesh volume за реальную мышечную массу.

Prospective hypothesis: сохранять origin до outcome, точные 14/30 дней, evidence availability/coverage, composition/shape/calibration versions и gaps. На CheckIn сопоставить реальные вес/обхваты/фото в одинаковых условиях; показывать actual date offset и missing fields. Отчёт: N, MAE, bias (fact − forecast), coverage диапазона, ошибки по horizon/maturity/subgroup. Межмодельное согласие Hall и synthetic tests не доказывают human accuracy. Late entries и outcome не должны попадать в старую calibration.

Renderer: оценивать только adherence к target future mesh, не его физиологическую правильность. Report accepted/rejected counts, p95 structural error/latency и cost per accepted endpoint с ракурсами/retries. Частично закрытое лицо/неподходящая поза могут делать metric неприменимой — это fail/unsupported, не автоматический pass. Примеры для ручного аудита включают отрицательные случаи и границы допустимых изменений.

### Автоматизация и отчётность новых фаз

Будущие domain tests покрывают provenance/state transitions/concurrency/migration/no-look-ahead; browser smoke — весь маршрут Avatar → today → Close Day → explicit Hypothesis → factual CheckIn на desktop/320/390 px с reload/ошибками. Расширять существующие skeletal/history/forecast/muscle-forecast/product/errors/offline suites по затронутому поведению; Stage A composition suite после merge #19 сохраняется. Реальные camera/touch/iOS/Safari и shape accuracy требуют отдельного ручного прохода.

Validation export расширять только implementation-фазой: opt-in closed-day revisions/coverage, Avatar lineage/derived labels, exact endpoint/evidence/gate metadata, outcome/calibration versions и renderer validation/cost receipts. Сохранять прежние privacy defaults: фото/имена/notes не включаются автоматически. Пока схема текущего package ниже не содержит этих новых сущностей.

## Протокол GO / NO-GO

Используйте обычный браузер по HTTPS или localhost. Повторите ключевые шаги на телефоне. Запишите версию из «Профиль → Диагностика и версия».

| Шаг | Действие | Что должно получиться |
|---|---|---|
| 1 | Открыть приложение в чистом browser context | Первый запуск, понятные пустые состояния |
| 2 | Ввести пол/дату рождения/рост/вес; часть замеров; перезагрузить на шаге 2; пропустить жир/фото; подтвердить | Тот же черновик после reload, один Manual BodySnapshot, только подтверждённые поля |
| 3 | Открыть Модель, выбрать обхват и ракурс | 3D, правильная лента; заполненные для визуализации пропуски явно обозначены |
| 4 | «Записать замеры», сохранить новую дату с частью полей | Частичный факт без автоматического копирования веса/жира/обхватов |
| 5 | Добавить кардио, сравнить активные ккал с часами | Запись на выбранную дату и понятные единицы |
| 6 | Добавить силовую с выполненным/невыполненным подходом и стороной | Учитываются только выполненные; тоннаж = внешняя масса × суммарные повторы |
| 7 | Выбрать дни календаря, исправить дату/минуты кардио, reload, удалить тестовую запись | Раздельные маркеры, правильные day/week totals; исправления сохраняются |
| 8 | Открыть нагрузку силовой в 3D | Для левой/правой стороны противоположный регион не окрашен; без клипа нагрузка всё равно работает |
| 9 | Задать питание и программу, включая упражнение без анимации | План сохраняется, недоступный клип обозначен текстом |
| 10 | Получить и сохранить прогноз, позднее добавить реальные факты | Диапазон, персональный прогноз и форма; старые версии неизменны, новые используют доступную историю |
| 11 | Сравнить две даты в Прогрессе, открыть фото-прогресс | Сохранённые факты, а не сегодняшние значения; оценки 3D отделены |
| 12 | Скачать полный backup с фото и всеми журналами | Один versioned ZIP с manifest и hashes |
| 13 | Восстановить ZIP в чистом browser context; открыть защищённые фото прежним PIN | Те же факты/прогнозы/программы/журналы, совпадающие файлы; отдельное подтверждение замены |
| 14 | Выбрать разделы и экспортировать validation package | Только выбранные данные; фото отсутствуют без отдельной галочки |

**NO-GO:** потеря/тихая перезапись данных, сохранение неизвестного жира как факта, изменение старого прогноза, данные на чужой дате, горизонтальный overflow страницы на 320 px, blank screen, отсутствие восстановления после сбоя. Реальная клиническая точность и «кг отдельной мышцы» не являются критериями этого релиза.

## Независимые контрольные данные

В «Профиль → Ручная проверка» доступны:

* Обхваты: введено / рассчитано на 3D / разница / источник. При отсутствии факта — «Оценка для 3D». Сравнивайте один уровень ленты, одинаковую позу и спокойное дыхание.
* Фото: независимая лента и оценка фото, абсолютная ошибка в сантиметрах; фиксируйте ракурс, одежду и предупреждения анализатора. Фотооценку не считайте независимой истиной.
* Калории: сохранённая оценка тренировки, активные ккал часов, разница; отдельная дата и необязательная внешняя оценка. Сравнивайте active с active, а не total. Часы тоже не являются безошибочным ground truth.
* Прогноз: выбранная версия и дата, последующие BodySnapshots, signed/absolute error. Проверка на истории использует заранее сохранённые версии и доступные тогда данные; для горизонтов 2/4/8 недель берётся ближайший факт ±3 дня.
* Форма: обхваты, фактически выполненная программа, записи reps/weight/RIR и проекция модели. Сравниваются сохранённые composition-only и training-aware точки. Изменение обхвата включает жир/воду/позу; истинный рост бицепса в килограммах не запрашивается.

Для регулярной проверки снимайте вес в сопоставимых условиях, обхваты повторяйте несколько раз и сохраняйте реальную дату. Не подгоняйте историю под желаемый прогноз. Базовые метрики: MAE, bias (факт минус прогноз), количество наблюдений, покрытие ожидаемого диапазона. Диапазон эвристический, не статистический доверительный интервал. Недостаток наблюдений отображается явно.

## Пакет анализа

`validation-package.zip` содержит manifest (format `trening-validation`, schemaVersion 1, app/build version, createdAt, SHA-256) и `analysis.json`. Все флажки изначально сняты:

| Выбор | Состав |
|---|---|
| Параметры профиля | Последний BodySnapshot ≤ сегодня: дата/пол/возраст/рост/вес/известный жир. Legacy-профиль без фактов маркируется как неподтверждённые настройки |
| История фактических замеров | Частичные поля, даты, источник, обхваты и метод; псевдонимы `fact-N` |
| Версии прогнозов | `forecast-N`, версии/параметры модели, входы плана, baseline/expected/ranges, calibration, muscle weeks и composition-only |
| Сводки тренировок | Даты/минуты/ккал кардио; силовые упражнения и выполненные подходы, reps/weight/side |
| Метрики и контрольные значения | Отчёт 3D, введённые tape/photo/external kcal и evaluation выбранной версии; связи через псевдонимы |
| Отдельно: фотографии | Только по явной галочке: расшифрованные полные JPEG под новыми последовательными именами; защищённые фото требуют разблокировки |

Локальные IDs, ссылки на фото, свободные notes/names, дата рождения, PIN, salt/verifier и encryption keys исключены. Пакет предназначен для анализа, не для восстановления. Даты и физические параметры всё ещё могут косвенно идентифицировать человека. Экспорт скачивает файл локально, никому его не отправляет.

## Автоматическая матрица

Release build: **0 warnings/errors**. .NET: **372/372**. JavaScript: **43/43**. Сохранены прежние 364 .NET и 36 JS сценариев; расширены контракты каталога и добавлены side/backup/privacy проверки.

| Browser smoke | Покрытие |
|---|---|
| skeletal | 104 кости, анимация, пять клипов, atlas/heatmap, ленты, поза, режимы, видео |
| strength | Подходы, журнал, агрегация и сохранение |
| history | Частичные факты, даты, сравнение, импорт и storage |
| forecast | Неизменяемые версии, персонализация, диапазоны, оценка фактов |
| muscle-forecast | Программа, морфинг/ограничения обхватов, диапазоны и архив |
| product | Fresh onboarding/reload, пропуски, отсутствие дублей; mixed calendar/edit/delete/reload; программа без клипа; encrypted photos; полный round trip/clean restore; сбои staged/local/photos; stale tab; interrupted tab; выборочный export |
| errors | MakeHuman failure с манекеном, atlas failure, localStorage/IndexedDB unavailable, quota exceeded, corrupt cardio с блокировкой записи |
| offline | Реальный publish и Service Worker из `/trening/`; offline reload, профиль, 3D/heatmap, программа/forecast, локальные фото, разделы и backup |

Product smoke проверяет **320×844 и 390×844**: Модель/Активность/Прогресс/План/Профиль и backup; onboarding проходит на 320 px, clean restore на 390 px. Проверяется ширина документа и внутренней панели. Локально — Windows Chrome/SwiftShader; CI — Ubuntu Chromium. Реальные iOS/Safari, touch/клавиатура и камера требуют ручного прохода по протоколу выше.

В CI запускаются все восемь suites. Для локального запуска: `dotnet build` и `dotnet test` решения в Release, `node --test tests/js/*.test.mjs`, затем browser scripts при запущенном Web на `APP_URL` (по умолчанию 5256). Для offline: `dotnet publish src/WorkoutCalculator.Web -c Release -o <publish>`, `node tests/browser/static-server.cjs <publish>/wwwroot 5257`, `node tests/browser/offline-smoke.cjs`. Нужен Playwright/Chromium; при локальном Chrome можно задать `BROWSER_CHANNEL=chrome`.

## Производительность

Сравнение с `main` после #15/#16 (`1ddb66e3`), одна машина, Release dev-server WASM, Chrome headless/SwiftShader, viewport 1400×950, профиль 85 кг/180 см, 12 недель и программа 3×3×10 bench press. Три новых browser context на вариант, четыре повторных входа в план; ниже медианы. Baseline инструментирован только измерением `RunForecasts`; вычислительное поведение не менялось. [Исходные измерения](PRODUCT_PERFORMANCE.json).

| Метрика, мс | main | Product consolidation |
|---|---:|---:|
| Навигация страницы → MakeHuman ready | 2146 | 2050 |
| MakeHuman resource duration (2 394 747 байт) | 495.5 | 449.0 |
| Assets ready, лог загрузчика | 793.2 | 735.7 |
| Atlas resource duration (107 156 байт) | 2.9 | 1.4 |
| Atlas чтение/проверка | 32.9 | 32.7 |
| Перестроение текущего тела | 65.0 | 66.9 |
| Полный расчёт прогнозов, включая program/shape pipeline | 194.9 | 198.2 |
| MakeHuman формы прогноза после muscle layer | 29.5 | 28.8 |

Небольшие изменения времени тела (+1.9 мс) и pipeline (+3.3 мс) зафиксированы; трёх выборок недостаточно для статистического вывода. Последний initial product smoke — 2.143 с. Переключение разделов в мобильном smoke: Модель/Активность/Профиль 17–43 мс, Прогресс 148–170 мс, План 436–490 мс. Это время полного Playwright click с обработкой и прокруткой, а не чистый render. План остаётся самым тяжёлым переходом; математическое ядро не оптимизировалось без доказанной причины. Локальные сетевые длительности не предсказывают первый запуск на мобильной сети.

## Offline и GitHub Pages

Первый запуск должен завершить установку Service Worker; после этого локально работают профиль/история/3D/журналы/прогнозы/heatmap/программы и сохранённые фото. MediaPipe для нового распознавания позы загружается при первом использовании и только затем доступен offline; локальная галерея не зависит от этого. Кэш и данные могут быть удалены браузером — backup остаётся необходимым. Backend/sync отсутствуют.

`base href="./"`, manifest и service worker используют относительные пути. Publish проверен под `/trening/`. На момент проверки репозиторий сообщает `has_pages: false`; [Pages run 37858210321](https://github.com/branch-danya-dev/trening/actions/runs/37858210321) успешно собрал/загрузил artifact, но deploy вернул 404 с указанием включить Pages. Это настройка размещения, не ошибка base path.

Точные действия владельца:

1. [Settings → Pages](https://github.com/branch-danya-dev/trening/settings/pages).
2. В **Build and deployment → Source** выбрать **GitHub Actions**.
3. **Actions → Pages → Run workflow → main**, либо повторить неуспешный deploy после включения.
4. После успешной публикации открыть `https://branch-danya-dev.github.io/trening/`, дождаться загрузки, затем проверить offline. Новая продуктовая версия появится там только после ручного merge её PR.

## Ограничения

Прогноз формы эвристический и симметричный; региональная L/R карта нагрузки не превращается в независимый прогноз роста левой и правой мышцы. Каталог — 50 описаний и только пять анимаций. Дни программы описательные, следующий элемент на главном экране — начало повторяющейся программы, без отдельного планировщика дат. Legacy-кардио хранит итоги, поэтому правка календаря не восстанавливает несуществующие исходные отрезки тренировки. Полный backup не является зашифрованным контейнером; его лимиты и восстановление описаны в [BACKUP.md](BACKUP.md).
