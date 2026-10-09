# Структура продукта: целевой lifecycle и текущая реализация

**Текущий слой Phase 5:** `ObservedHypothesisStore` / `workoutcalc.observedHypotheses.v1` — единственный owner наблюдаемых гипотез, embedded immutable ForecastSnapshots и outcome events. «План» содержит review/save, «Прогресс» — историю/результат. Legacy `workoutcalc.hypotheses.v1` остаётся owner ручных сценариев. [Полный source-of-truth контракт](HYPOTHESIS_LIFECYCLE.md).

## Источники истины Phase 3 / 4

ActivityDay реализован поверх существующих журналов, без новых ForecastInput/ForecastSnapshot полей. `DayPlan is expectation; ClosedActivityDay is fact.` Полная [матрица и state machine](ACTIVITY_DAY.md).

| Данные | Owner |
|---|---|
| Дата/Profile/cycle/state/DayPlan/default template | `workoutcalc.activityDays.v1`, один CAS envelope |
| Лёгкие физические события | ActivityEvent в этом envelope |
| Силовая / кардио | Existing TrainingSession / LoggedWorkout; ActivityDay хранит reference |
| Подтверждённая дневная сводка | Frozen ClosedActivityDay; underlying journal edits закрытой даты запрещены |

«Активность» открывает сегодня; календарь показывает пустые прошлые и будущие даты. План отделён от факта. Шаблон доступен в «Профиле». Новые события/close не вызывают shape builder; карта дня использует прежний muscle engine/atlas. Старые dated workouts индексируются как Open/needs review, не Completed. Phase 4 Nutrition реализована: `DayPlan.MealSlots` — ожидание; `ActivityDay.Meals` — фактические snapshots продуктов и съеденные граммы; `ClosedActivityDay.Nutrition` — frozen evidence. `NutritionTarget` — read-only view существующего сохранённого Forecast plan, нового target store нет. Schema 2 envelope атомарно владеет едой и активностью. [Полная source-of-truth matrix](NUTRITION_V1.md#model-and-ownership). Phase 5+ остаётся планом.

## Источники истины после Phase 1

Phase 1 реализована и смержена в `main 932a2aa`; Phase 2 смержена в `main 6926da7`. Исторический текст ниже о будущих фазах уточняется статусом Phase 3 выше.

| Данные | Единственный writable owner |
|---|---|
| Системный профиль, пол/рост/возрастной источник, goal/settings | `Profile`, Profiles collection в `workoutcalc.avatarDomain.v1` |
| Фактические body observations | `BodySnapshotStore`, `workoutcalc.bodySnapshots.v1` |
| Рабочая форма и frozen reconstruction inputs | `AvatarRevision`, Avatars collection того же atomic envelope |
| Ручная визуальная коррекция | `AvatarShapeCorrectionProfile` в draft/revision; не factual сантиметры |
| Производные mesh metrics | `AvatarDerivedMetrics`, `Source=AvatarDerived` |
| Прогнозы и calibration history | `ForecastStore`, `workoutcalc.forecasts.v1`; optional frozen AvatarOrigin |
| Photo blobs и метаданные фотосессий | Existing IndexedDB photo store; revision хранит только refs/metadata |

Profiles/Avatars фиксируются одним CAS commit для целостности ссылок; детали — [AVATAR_DOMAIN](AVATAR_DOMAIN.md). Frozen факт в revision — snapshot для replay, не редактируемая копия BodySnapshotStore. Legacy `body.v1/preferences.v1` после migration остаются before-images. `BodyProfile` — вычислительный адаптер.

Текущий 3D использует сохранённую revision или явно открытый draft preview. Свободная настройка active body заменена действием «Исправить аватар». После manual confirm требуется явное начало нового forecast cycle; старые forecasts сохраняются. Единый восьмишаговый onboarding собирает факты, предлагает front/side photos, показывает 3D corrections и provenance/quality review; только `Зафиксировать аватар` завершает создание. Photo revision API подготовлен; новый check-in UI ещё не подключён. Текст ниже о старой реализации после #18 — исходная база, а не новый источник истины.

Главный продуктовый ориентир — [PRODUCT_LIFECYCLE](PRODUCT_LIFECYCLE.md). Порядок разработки — [PRODUCT_LIFECYCLE_ROADMAP](PRODUCT_LIFECYCLE_ROADMAP.md), выбор инструментов — [PRODUCT_TECHNOLOGY_DECISIONS](PRODUCT_TECHNOLOGY_DECISIONS.md). Новые правила ниже являются спецификацией, а не реализованными возможностями main.

## Целевой маршрут

**Profile → Avatar → shape review/correction → Confirm/Lock → ActivityDay → Close Day → Hypothesis 14/30 дней → optional endpoint Render → CheckIn → Forecast vs Fact.**

| Раздел | Целевая роль |
|---|---|
| Профиль | Системная учётная запись/настройки, active Avatar, приватность, backup. Profile не является телом. |
| Создание Avatar / Модель | Measurements и optional photos → reconstruction → обязательный 3D review → semantic correction → подтверждение. В обычном режиме locked; ручная перестройка только в Recalibration Session с новым tracking origin. |
| Активность | Главный экран после Avatar: сегодня в непрерывном календаре, template, реальные события и питание, review/Close Day. MissingData отличается от RestDay. |
| Прогресс | Фактические CheckIn/BodySnapshot, current Avatar revisions и архив. Новые фото обновляют current revision автоматически после quality gate, сохраняя origin/историю. |
| Гипотеза (раздел План) | Явное создание versioned прогноза из closed evidence на 14/30 дней; maturity/uncertainty, одна конечная точка, check-in и сравнение с фактами. Незакрытый daily plan не вход forecast. |

Nutrition v1 — ручные КБЖУ и пропорциональный пересчёт массы порции. Meal time организационный, без food quality score. Render — optional milestone с evidence/Avatar/photo gates; local GeometryWarpRenderer baseline, managed AI позже. Автономный coach, mental UX и постоянный GPU на старте не входят в scope.

Рабочий Avatar включает подтверждённые shape corrections, но factual сантиметры не изменяются. Mesh girths и leanness/muscularity proxies — `AvatarDerived`/priors, не измеренный BF% или масса мышц. Закрытый день и Hypothesis используют frozen revisions; исправления создают новые версии.

## Реализованная база после #18

Этап собран на `main` после последовательного merge #15 и #16 (`1ddb66e3`). Новых моделей состава тела не добавлено. Сохранены skeletal viewer, пять анимаций, атлас мышц, журналы, история и неизменяемые прогнозы.

Сам #18 затем смержен в main `4714497`; #17 с Forecast v3 R&D также смержен. Проверка 2026-10-09: #19 Stage A находится на review. Разделы ниже фиксируют существующий маршрут #18; **он ещё не реализует** отдельные Profile/Avatar revisions, lock/recalibration, ClosedActivityDay, nutrition journal, maturity gates и новый Hypothesis lifecycle. Нынешние mutable plan scenarios не равны новой Hypothesis. При расхождении поведения старого UI и целевой модели руководствоваться каноном и фазами миграции, сохраняя legacy replay.

## Разделы и основной маршрут

| Раздел | Что делать |
|---|---|
| Модель | Посмотреть текущую форму, последний измеренный вес, изменение за 30 дней, тренировки недели, цель и следующий элемент программы. Записать замеры, добавить фото, включить ленты, упражнение и нагрузку мышц. Внешний вид 3D настраивается отдельно. |
| Активность | Выбрать день календаря, записать кардио или силовую, исправить/удалить запись, посмотреть день и неделю, открыть нагрузку тренировки в 3D. |
| Прогресс | Сохранить частичный BodySnapshot, выбрать дату или сравнить две даты, посмотреть факты веса/жира/обхватов, тренировочную сводку и фото-прогресс. |
| План | Задать питание, кардио и силовую программу, посмотреть прогноз веса и изменения формы, ожидаемые диапазоны, сохранить версию и сравнить с последующими фактами. Проверка базового/персонального прогноза доступна здесь же. |
| Профиль | Дата рождения, цель, текущие параметры, приватность фото и PIN, полный backup/restore, ручная проверка и экспорт пакета анализа. Версии и технические параметры — в раскрываемой диагностике. |

Навигация находится сверху на desktop и снизу на телефоне. История и прогноз доступны напрямую, без classic/debug. Таблица или содержимое панели могут прокручиваться внутри; страница не расширяется за 320 px.

Маршрут: **первый запуск → Модель → запись активности → Прогресс → План → повторные замеры → проверка прогноза**. Перед переносом устройства и ручным экспериментом — полный backup.

## Первый запуск

Актуальный Phase 2 маршрут: Profile facts → optional girths/BF% → optional photos → goal → confirmation of factual snapshot → Avatar correction → review/lock. Draft и шаг переживают reload/close/restore; второй мастер не создаётся. Controls: ширина плеч, грудь, живот, бока, талия, ягодицы, руки, ноги, глубина корпуса и сутулость. Каждый имеет neutral/reset; normal locked view содержит дату/quality/history и отдельное `Исправить аватар`. Фотографии спереди/сбоку уточняют только отсутствующие поддерживаемые girths; back не анализируется. Подробности — [AVATAR_DOMAIN](AVATAR_DOMAIN.md). Старое описание базового onboarding ниже — исторический контекст #18.

Шесть шагов: пол/дата рождения/рост/вес; восемь необязательных обхватов; известный процент жира или «Не знаю»; фото front/side или пропуск; цель; подтверждение введённых фактов. Фокус поля обхвата подсвечивает соответствующую ленту на 3D. Цель меняет только исходные настройки плана.

Черновик сохраняется при вводе и смене шага. Reload возвращает тот же шаг и стабильный ID первой записи. Завершение сохраняет профиль и один Manual BodySnapshot; повтор после прерывания не создаёт второй ID. Незавершённый черновик остаётся до успешного сохранения всех обязательных данных. Изменившаяся в другой вкладке версия черновика защищена от перезаписи.

На первом шаге можно восстановить полный backup вместо создания нового профиля. Существующий legacy-профиль сам по себе не превращается в измеренную историю; его явный импорт остаётся в «Прогрессе».

## Факты, оценки и прогнозы

* **Факты** — частичные BodySnapshots, с датой, происхождением и только известными полями. Текущий факт — последняя запись не позже сегодня; порядок при одинаковой дате определяется ID. Пропуски не заполняются фактами из будущего или из других дат.
* **Оценки для 3D** — результат `SnapshotVisuals.Build` для недостающих полей. Они перечислены в подписи. «Не знаю» для жира остаётся null в истории. Изменение внешнего вида и подгонка по фото не сохраняются автоматически как ручной замер.
* **Прогнозы** — неизменяемые ForecastSnapshots с исходными данными, версиями модели и калибровки, точками и диапазонами. Новые факты могут изменить следующий прогноз, но не пересчитать архивный.

Новая запись замеров начинается без прежнего веса, жира и обхватов; из текущего факта копируются только пол/возраст/рост. Сохранение редактора — явное подтверждение введённых значений. Просмотр прошлой даты не заменяет текущий профиль. Старый `AtOrEarliest`/future-fallback из #10 не используется.

Кардио отображается на сохранённую дату, с сохранёнными итогами. Правка даты/длительности/ккал — явное исправление журнала, без неявного пересчёта по сегодняшнему весу. Силовые агрегируются из существующих TrainingSessions. Новая параллельная workout-модель не вводилась.

## Календарь и нагрузка

Месячная сетка начинается с понедельника: отдельные маркеры кардио и выполненной силовой, минуты, активные ккал кардио, выполненные подходы. День содержит список записей, общие минуты, активные ккал, подходы/повторы/внешний тоннаж и мышечную нагрузку. Неделя содержит количество/минуты/ккал кардио, количество/подходы силовых и основные мышечные группы.

Каталог содержит **50 упражнений, 5 анимаций**. Каждое упражнение имеет уникальный ID, движение, оборудование, primary/secondary/stabilizer группы, признак unilateral, вариант/примечание и необязательный animationId. Упражнения без клипа работают в журнале, программе, heatmap и прогнозе; интерфейс сообщает «3D-анимация пока недоступна».

Сторона задаётся для подхода: обе / левая / правая / поочерёдно. У поочерёдного подхода повторы — **суммарные**, нечётный последний относится к левой стороне. Тоннаж не удваивается. Региональная нагрузка L/R суммируется до насыщения; невыбранная сторона не нагружается. Общая группа использует среднюю экспозицию сторон. Прогноз роста/формы остаётся симметричным: это явно обозначено в редакторе программы, даже если карта нагрузки асимметрична. Классификация упражнений и нагрузка эвристические, не EMG.

## Хранение и границы cleanup

Сохранены текущие strict stores/CAS для истории, силовых и прогнозов, legacy localStorage для профиля/планов/кардио и IndexedDB v2 для фото. Общая защита записей блокирует повреждённый store, конкурентный restore и устаревшую после restore вкладку. Продуктовое состояние вынесено в `Home.Product.cs`; календарь, обзор, мастер, backup и проверка — отдельные компоненты. Старую ветку не копировали и архитектуру не переписывали.

Полный состав данных, восстановление и ограничения описаны в [BACKUP.md](BACKUP.md). Ручной протокол, offline/Pages, тестовая матрица и измерения — в [VALIDATION.md](VALIDATION.md). Сопоставление с #10 — в [PR10_AUDIT.md](PR10_AUDIT.md).

## Phase 5 source of truth

Plan hosts ObservedHypothesisPanel (review/save) above legacy manual Scenarios. Progress hosts historical hypotheses plus factual BodySnapshot editing. No sixth navigation item. WorkoutCalculator.BodyModel.Hypotheses owns lifecycle/policies; ObservedHypothesisStore owns workoutcalc.observedHypotheses.v1 with embedded immutable ForecastSnapshots, calibration and append-only events in one CAS. Existing workoutcalc.hypotheses.v1 remains the scenario store; no evidence migration. ActivityDay/Avatar/body facts retain their owners; linked outcome facts are protected. See [HYPOTHESIS_LIFECYCLE](HYPOTHESIS_LIFECYCLE.md).
