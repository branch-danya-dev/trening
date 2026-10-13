# Как проверять приложение

## Pre-UI stabilization: semantic selector policy

The [pre-UI contract](PRE_UI_CONTRACT.md) and [readiness report](PRE_UI_READINESS.md) govern the next UI refactor. Preserve all existing behavioral suites; update screenshot/layout expectations separately.

1. Prefer accessible role/name for user actions and explicitly labelled inputs. Copy changes may update these names with a reviewed test change.
2. Use stable `data-testid` for state surfaces or repeated domain rows: `checkin-new`, `checkin-save`, `checkin-result`, `checkin-panel`, `observed-hypothesis`, `hypothesis-comparison`, `geometry-warp`, `render-output`, `strength-exercise`, `strength-set`, `pwa-update-ready`. Keep existing day state/date attributes.
3. Do not add CSS hierarchy, nth-child, card positions or pixel coordinates to behavioral assertions. An index within a semantic ordered exercise/set collection is acceptable when order itself is tested.
4. CSS/scroll width/screenshot assertions remain in explicitly labelled layout sections. A moved card should not invalidate factual backup/CAS/hash assertions.
5. Share basic workflows through `tests/browser/product-page.cjs`; selectors are test infrastructure, never application business logic.

Audited suites: avatar, avatar-creation, skeletal, strength, history, forecast, composition, muscle-forecast, product, activity-day, nutrition, hypothesis, checkin, errors, anatomical-muscle smoke/audit, GeometryWarp benchmark/WebGL/smoke and published offline. Most already use role/name and state IDs. Changes target check-in orchestration/focus and strength row scopes; remaining `.panel-body`/card width selectors intentionally measure layout. No blanket test rewrite.

Generate scenarios and native timings:

```sh
dotnet run -c Release --project tools/WorkoutCalculator.PreUiAudit -- . work/pre-ui-fixtures
dotnet run -c Release --project tools/WorkoutCalculator.GeometryWarpBenchmark -- . work/geometry-fixtures.json
```

With a Release app running, set `APP_URL`, `PRE_UI_FIXTURES`, `PRE_UI_OUTPUT`, `GEOMETRY_FIXTURES` and Playwright's `NODE_PATH`, then run `pre-ui-storage.cjs`, `pre-ui-fixtures.cjs`, `pre-ui-mixed.cjs`, `pre-ui-history.cjs`. Run the timing suite alone to avoid CPU contention. `pwa-version-transition.cjs` starts its own server and requires `PWA_ROOT` pointing to published wwwroot. CI runs them sequentially and uploads evidence, including native timings and full synthetic backup.

Fixture catalog: brand-new, avatar-draft, locked-empty, seven-days, nutrition-partial/complete, preliminary/preliminary-issued, active-14d, awaiting-outcome, evaluated, expired, checkin-good-photo/bad-photo, geometry-eligible/unsupported, large-history and quota-warning. `loadScenario(context,url,file,{enabled:true,...})` requires a new isolated context and loopback URL; geometry recipes additionally require `geometryFixtures` and `output`. No loader or seeds ship in the app. Exports in explicit fixture mode are marked `syntheticFixture:true` and rejected outside loopback plus sessionStorage `trening:dev-fixtures=enabled`; research remains a separate disabled flag. General fixture generation preserves semantic states but new issued IDs need not be byte-identical across generation; the checked-in canonical ZIP is the exact replay reference. [Canonical import and measurement instructions](evidence/pre-ui/README.md).

Accessibility baseline is local assertions, with no added dependency: visible controls named/labelled, unique IDs, check-in keyboard operation and focus transitions, units/context, error association, state roles/headings. This is not a full WCAG audit. Screen-reader review, every modal's focus sequence, contrast and visual interaction design belong to the separate UI phase.

History budget is a measured regression guard, not a phone SLA: 1000/fresh CheckIn save <=8x and 1000/365 <=3.5x. The desired <=2x ratio is reported separately and is **not currently met**; whole-envelope persistence and post-save factual projections remain linear. Do not silently loosen the ceilings or compare timings produced under concurrent benchmark runs.


Current decision, 2026-10-09: the non-AI R&D package is ready for final review. Product lifecycle Phases 1–7 are implemented; Phase 8 managed renderer and Phase 9 GPU/business work remain deferred. C0/prospective infrastructure exists; C1 remains `BLOCKED_PENDING_DATA_ACCESS`, Pseudo-DXA is blocked by prerequisite/rights. Procedural shape and muscle remain production defaults; BodyParts3D is research NO-GO for default. No participants recruited, no application submitted, no renderer integration. [Current validation matrix](MODEL_VALIDATION_STATUS.md), [hardening and reproduction](MODEL_PRODUCTION_HARDENING.md), [readiness for later controlled tests](USER_TEST_READINESS.md) supersede the historical planning status below.


Forecast v3 composition: [формулы и ограничения](FORECAST_V3_COMPOSITION.md), [автоматические и ручные проверки Stage A](VALIDATION.md#composition-v3--stage-a), [воспроизводимый benchmark](FORECAST_V3_COMPOSITION_BENCHMARK.md). Макросы необязательны; sodium/ECF в production выключен. Ранее сохранённые прогнозы воспроизводятся без пересчёта.

Чек-лист для проверки приложения на реальных данных: что открыть, что проверить
и что прислать, если что-то не так. Снимки при этом не покидают устройство: для обратной связи хватает
цифр из «Отчёта для проверки».

## Где открыть

| Где | Как | Камера и PIN-код |
|---|---|---|
| Телефон | https://branch-danya-dev.github.io/trening/ — после включения GitHub Pages (ниже) | да |
| Компьютер | `dotnet run --project src/WorkoutCalculator.Web` → http://localhost:5256 | да (веб-камера) |
| Телефон по локальной сети | `dotnet run … --urls http://0.0.0.0:5256` → http://<адрес компьютера>:5256 | **нет** — браузер не даёт их странице без HTTPS |

**Один раз включить GitHub Pages:** Settings → Pages → Build and deployment → Source: **GitHub Actions**.
Затем Actions → Pages → последний запуск → **Re-run all jobs** (или любой пуш в main). Через пару минут
приложение откроется по адресу выше. На телефоне его стоит установить: Safari — «Поделиться → На экран
„Домой“», Chrome — «Установить приложение». Так браузер не сотрёт фото при нехватке места.

**Какая версия открыта.** Внизу вкладки «Параметры» — «сборка xxxxxxx»: первые 7 знаков коммита. После
пуша в main установленное приложение скачивает новую версию в фоне и показывает плашку «Вышла новая
версия · Обновить».

## Что проверить

### 1. Замеры и модель — вкладка «Параметры»

- [ ] Ввести свои пол, возраст, рост, вес, % жира и обхваты (лента — там, где показывает галочка «ленты
      замеров»). Модель похожа по пропорциям, подсказки под замерами разумные.
- [ ] Шея указана → под % жира оценка по формуле ВМС США; близка к вашим весам или DEXA?
- [ ] «Осанка и форма»: ползунки меняют силуэт сбоку плавно, без изломов (особенно сутулость и прогиб).
- [ ] Переключатель «MakeHuman / Манекен» работает, выбор запоминается после перезагрузки.

### 2. Расчёт тренировки — вкладка «Тренировка»

- [ ] Ввести реальную тренировку с дорожки (скорость, уклон, время) и данные с часов. Сравнение
      «Apple Watch: … на N% больше/меньше оценки» правдоподобно, методы в таблице «Как считали»
      не расходятся в разы.
- [ ] «Скорость или уклон менялись»: несколько отрезков, «+ Отрезок» и «×» работают; ошибка в поле
      подсвечивает его и ставит в него курсор.
- [ ] Улица: дистанция, время и набор высоты из часов; дистанция по часам и расчётная близки.
- [ ] Пульс покоя и VO2max на первом шаге сохраняются и после перезагрузки на месте.
- [ ] «Сохранить в журнал» → тренировка в списке и в итоге недели; дата меняется; удаление
      с подтверждением. Если в гипотезе есть кардио — строка «На этой неделе — N из M».
- [ ] Переключиться на другую вкладку и обратно посреди расчёта — введённое не пропадает.

### 3. Прогноз — вкладка «Гипотеза»

- [ ] Свой реальный план: калории, активность, тренировки. Итог и предупреждения правдоподобны.
- [ ] «+ Копия» → изменить калории → сравнение гипотез и линии на графике веса различаются как ожидалось.
- [ ] «Начать план сегодня», затем несколько дней записывать вес («Вес сегодня»). Кружки фактов на графике,
      таблица «Факт против прогноза» и итог «впереди / позади».
- [ ] 3D-вид: режимы «Прогноз» и «Сравнение», кнопка «● Видео» сохраняет файл и он открывается.

### 4. Фото — главное, что нужно проверить на живых людях

**Условия съёмки:** облегающая одежда или бельё, однотонный фон, ровный свет, волосы собраны, без обуви;
телефон вертикально на уровне пояса в 2–3 м. Спереди — руки чуть в стороны; **сбоку — руки вдоль тела,
ноги вместе** (разведённые стопы сбивают масштаб на 4–5 %).

- [ ] «Снять с подсказками»: подсказки понятны, снимок делается сам. Или «Фото спереди / сбоку» из галереи.
- [ ] После «Сохранить сессию» разбор запускается сам (первый раз — до минуты): бирюзовый контур идёт по телу, а не по фону или одежде; точки у рук — там, где
      рука касается тела.
- [ ] **Обхваты по фото против ленты** (таблица разбора). Талия и бёдра по фото должны быть в пределах
      заранее согласованного исследовательского допуска. Записать фактическую ошибку в сантиметрах и источник;
      ±2–3 см не являются подтверждённой точностью приложения. ANSUR II не заменяет проверку на независимых фото.
- [ ] **Повторяемость:** две сессии подряд за 5 минут (отойти и встать заново) → «Сравнить с:» → изменения
      записать наблюдаемую разницу. Порог 0,5 см и уровень шума пока не подтверждены на независимых участниках.
- [ ] «Подогнать модель по фото»: расхождение силуэтов уменьшается, оранжевый пунктир модели ложится на
      контур, осанка на вкладке «Параметры» похожа на вашу (прогиб, сутулость). «Вернуть как было» работает.
- [ ] Через неделю-две — новая сессия: карта изменений и обхваты «было → стало» совпадают с лентой
      по знаку и порядку величины.
- [ ] Чип «Прогноз: …» над снимками: снимок меняется плавно, без ступенек и разрывов у бёдер и шеи.

### 5. Защита фото — карточка «Защита»

- [ ] Установить PIN-код → «Закрыть снимки» → вместо превью замки; неверный PIN-код — ошибка, верный
      открывает.
- [ ] Свернуть приложение больше чем на 2 минуты → снимки снова закрыты.
- [ ] Перезагрузить страницу → снимки закрыты.
- [ ] «Размывать снимки» и водяной знак видны; в переключателе приложений снимки размыты.
- [ ] «Сохранить архив (zip)» и «Загрузить архив» на другом устройстве или в другом браузере.

### 6. Офлайн

- [ ] Установленное приложение без интернета (авиарежим) открывается, модель строится, фото на месте.

## Персонализированный прогноз

Автоматический сценарий `tests/browser/forecast-smoke.cjs` входит в CI вместе с проверками skeletal,
strength и history. Он сохраняет прогноз, добавляет факты нескольких недель, проверяет новую калибровку,
доступность baseline, диапазон и 3D, неизменность старой версии после новых фактов и перезагрузки,
защиту от второй вкладки/повреждённого хранилища и ширины 320/390/1400 px.

Вручную: во вкладке «Гипотеза» сохранить прогноз; последующие замеры вносить в «Историю».
После минимум 4 недельных групп за 21 день (первое наблюдение не раньше 14 дней от начала) проверить
статус калибровки и создать новый прогноз. Старая версия должна показывать прежние точки и диапазон.
Прежние начатые планы переносятся отдельной кнопкой как восстановленные; они не участвуют в обучении.
Формулы и ограничения: [PERSONALIZED_FORECAST.md](PERSONALIZED_FORECAST.md).

## Что прислать

- **«Отчёт для проверки»** — кнопка под разбором фото, рядом с «Разобрать заново». Только цифры: профиль,
  ширина и глубина на уровнях груди, талии и бёдер, обхваты по фото против ленты, итог подгонки,
  сборка, браузер и экран. Ни снимков, ни контуров в нём нет. «Скопировать» — и вставить в сообщение.
- Скриншот, если что-то выглядит неправильно: с включённым размытием снимки на нём не видны, контуры остаются.
- Что делали, что ожидали, что получилось; устройство и браузер.

## Известные ограничения

- Обхват груди по фото не считается: в ANSUR II ширину груди мерили иначе, чем её видно на снимке.
- На модели MakeHuman формула завышает бёдра на 5–6 см (сечение бёдер у неё «площе», чем у людей); насколько
  точно на людях — как раз то, что нужно проверить.
- Прогноз на фото не меняет лицо, руки и ноги — это иллюстрация, а не обещание.
- Запретить снимок экрана веб-страница не может; PIN-код защищает от чужих рук, а не от взлома телефона.

## Проверка создания аватара — Phase 2

1. В чистом браузере заполните пол, дату рождения, рост и вес; пройдите необязательные обхваты/BF%. Неизвестные поля оставьте пустыми. Проверьте подсветку ленты при фокусе поля.
2. На фото-шаге можно пропустить фото либо добавить front/side и нажать `Уточнить черновик по фото`. Предупреждение/низкое качество не должны менять прежнюю форму. Фото со спины не предлагается.
3. Подтвердите факты и нажмите `Создать 3D аватар`. Отдельно проверьте плечи/грудь/живот/бока/талию/ягодицы/руки/ноги/глубину корпуса/сутулость, числовой ввод, клавиатуру и reset. Фактические сантиметры не меняются. На 320/390 px 3D остаётся сверху; группа controls сворачивается.
4. Перезагрузите страницу или закройте вкладку на любом шаге. На стадии коррекции создайте полный backup из `Резервная копия настройки` и восстановите в чистом браузере. Шаг, draft и фото должны вернуться.
5. `Проверить аватар` показывает измеренные/по фото/оценённые значения, corrections и ограничения. Только после просмотра checkbox разрешает `Зафиксировать аватар`. В обычном Model нет свободных shape controls.
6. `Исправить аватар` открывает новую сессию с 3D preview. При необходимости запишите новый factual measurement через историю, затем `Использовать последний факт`. После новой фиксации прежняя ревизия и forecasts остаются в истории, текущий forecast cycle требует отдельного подтверждения.
7. В Profile проверьте `Avatar Validation`, optional similarity 1–5, полный backup/restore locked avatar и opt-in validation export без фото.

Автоматизация: `node tests/browser/avatar-creation-smoke.cjs` при запущенном Web (`APP_URL`). `AVATAR_OUTPUT` задаёт каталог screenshots и JSON отчёта; `BROWSER_CHANNEL=msedge` или `chrome` допустим локально, CI использует Chromium. Cold/warm builder timings не включают 180 мс debounce и не являются замером физического телефона. Полный CI включает также skeletal, strength, history, forecast, muscle-forecast, product, avatar, errors и published offline `/trening/`.

## ActivityDay — Phase 3

Phase 4 adds `NutritionTests`, `NutritionStorageTests`, `tests/js/nutrition.test.mjs` and `tests/browser/nutrition-smoke.cjs`. Run `dotnet test WorkoutCalculator.sln -c Release`, `node --test tests/js/*.test.mjs`, then the existing browser matrix plus `node tests/browser/nutrition-smoke.cjs`. `NUTRITION_OUTPUT` chooses the screenshot/report directory. CI uploads `nutrition-evidence`; the published offline suite also adds calories-only food, closes the day and verifies its frozen partial summary after reload. Required regression matrix now has 13 suites including offline. Exact results and environment: [Nutrition evidence](evidence/nutrition/README.md).

Nutrition smoke covers serving 350→175, per100g, second entry, decimal comma, gram edits, plan/actual separation, walking coexistence, explicit completeness, immutable closure, stale tab, rest with meals, missing≠zero, exact clean restore, private validation export and unchanged geometry UUID. Unit/storage tests cover migration of actual missing Phase 3 JSON fields, same-tab stale review/plan/event guards, future schema and checksum-valid tampering, atomic moves, unknown macros and no-look-ahead aggregation. Old ForecastSnapshot replay and Hall benchmark remain required.

`node tests/browser/activity-day-smoke.cjs` при работающем Web; `ACTIVITY_OUTPUT` задаёт каталог отчёта/screenshots, `APP_URL` — адрес, `BROWSER_CHANNEL=msedge` допустим локально. CI использует Chromium и сохраняет artifact `activity-day-evidence`.

1. После locked Avatar откройте «Активность»: сегодня, полный календарь, отдельные «План» и «Факт». Исторические strength/cardio требуют review.
2. Добавьте ходьбу (km/steps, optional minutes), растяжку и короткое упражнение из каталога. План сам не становится выполненным; без duration/distance калории неизвестны. Измените исходную тренировку в открытом дне: summary должен обновиться.
3. «Закончить день» → review → подтверждение. Проверьте сумму времени, source kcal coverage, plan adherence и дневную карту на аватаре. После close нет обычного редактирования, вторая вкладка также не может изменить sources.
4. Пустой прошлый день явно закройте как отдых; другой — «Нет данных». Пустые даты не превращаются в отдых сами. Будущий день не создаёт records и не закрывается.
5. Reload, full backup, clean restore: exact day states/closures/source IDs, без дубликатов. В validation export отдельно выберите дни: нет notes/raw IDs. Проверьте 320/390/1400 px.

Автоматические проверки: 452 .NET, 54 JS, новый ActivityDay browser suite плюс десять прежних suites (включая published offline). [Контракт](ACTIVITY_DAY.md), [evidence](evidence/activity-day/report.json). Измерения — desktop headless Edge/SwiftShader; физический телефон требует отдельной ручной проверки.

## Phase 5 Hypothesis tests

Run dotnet test WorkoutCalculator.sln -c Release, node --test tests/js/*.test.mjs, and tests/browser/hypothesis-smoke.cjs against the running Release app with Playwright. HYPOTHESIS_OUTPUT selects screenshot/report output. The new smoke covers insufficient/preliminary gates, explicit 14/30 save, unchanged origin after another close/reload, stale review and one-active rejection, 3D endpoint, target outcome with weight only, expiry, recalibration and exact clean restore/private export at 320/390/1400px. It uses fixed calendar dates and genuine UI meal/day closure; fixtures reuse the resulting archives for later calendar states. Keep the existing 13 suites including nutrition/activity/offline. Evidence: [Hypothesis](evidence/hypothesis/README.md). Domain policies and test boundaries: [HYPOTHESIS_LIFECYCLE](HYPOTHESIS_LIFECYCLE.md).

## Phase 6 — Current Avatar check-ins (2026-10-09)

Run `dotnet test WorkoutCalculator.sln -c Release`, `node --test tests/js/*.test.mjs` and the existing CI browser matrix with added `tests/browser/checkin-smoke.cjs` (`CHECKIN_OUTPUT` selects evidence output). Coverage includes partial facts, unsupported photo-only envelopes, good/bad/conflicting pairs, immutable priors/forecasts, stale/out-of-order processing, every journal failure boundary, optional outcome attachment after reload and exact clean restore. The published offline suite includes manual and cached-analysis PhotoDerived check-ins. Optional `checkin-photo-performance.cjs` measures cold/warm no-person rejection only. Current evidence: **607 .NET, 72 JS, 15 browser suites**, [report and 320/390/1400 screenshots](evidence/checkin/README.md). Physical low-end phone and empirical accuracy remain unvalidated.
## Phase 7 — GeometryWarpRenderer

Release: `dotnet build WorkoutCalculator.sln -c Release`, `dotnet test WorkoutCalculator.sln -c Release --no-build`, `node --test tests/js/*.test.mjs`. Generate deterministic geometry from repository assets: `dotnet run -c Release --project tools/WorkoutCalculator.GeometryWarpBenchmark -- . work/geometry-fixtures.json`.

При работающем Web (`APP_URL`) запустить `tests/browser/geometry-warp-benchmark.cjs`, `geometry-warp-webgl.cjs`, `geometry-warp-smoke.cjs` через Node/Playwright. `GEOMETRY_FIXTURES` задаёт JSON meshes, `GEOMETRY_OUTPUT` — evidence; `BROWSER_CHANNEL` optional, CI uses Chromium. Published PWA дополнительно запускает тот же smoke с `GEOMETRY_PWA=1` и `APP_URL=http://127.0.0.1:5257/trening/`: cold page → SW controlled → offline reload → local render.

Сохранить всю прежнюю regression matrix Phase 6. Новые проверки: 88 structural cases against NoWarp и настоящий legacy row warp; pinned identity, depth occlusions, shader/context failures, deterministic repeat, source-policy/integrity, frozen UI flow, PIN/full-backup exact, stale-generation writes, source cascade, exported synthetic rejection, network audit и 320/390/1400. CI uploads `geometry-warp-evidence`, включая published PWA. [Результаты/границы](evidence/geometry-warp/README.md), [политики](GEOMETRY_WARP.md).


## Anatomical muscle fields — Stage B

Stage B anatomical tests (2026-10-09): run the full Release/.NET/JS suites, then `WorkoutCalculator.AnatomicalMorphBenchmark`, `tests/browser/anatomical-muscle-smoke.cjs` and `anatomical-muscle-audit.cjs` with `ANATOMY_OUTPUT`. CI uploads anatomical evidence and runs all previous suites. GeometryWarp smoke covers absent legacy provider metadata and a pinned anatomical endpoint (`GEOMETRY_ANATOMICAL=1`), including backup and replay. Published offline smoke checks exact bundled sidecar SHA. Parser fixtures reject SHA/path/duplicate/index/finite errors; geometry checks cover identity, protected regions, caps, joints, spatial differentiation, symmetry, girth fallback and immutable mass outputs. Rebuild the real source twice intentionally, outside CI; hashes must match. [Commands and limits](ANATOMICAL_MUSCLE_FIELDS.md).
