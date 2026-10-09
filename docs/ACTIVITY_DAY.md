# ActivityDay — Phase 3

База: `main 6926da7330792eddf0bdbf56bfdc08035e92f06c`, обычный merge #22. [CI базы](https://github.com/branch-danya-dev/trening/actions/runs/37883782975) прошёл. #19 остаётся отдельным PR (`62ad4811169d8ea8c3de22247c4ce0712b9c7763`); его формулы и benchmark в эту ветку не входят. ForecastInput/ForecastSnapshot не изменяются.

**DayPlan is expectation; ClosedActivityDay is fact.** Этот PR создаёт пригодный для будущего потребителя контракт, но не подключает его к Forecast/Hypothesis и не изменяет сохранённые прогнозы.

## День и состояния

`Locked Avatar → Calendar → Open + DayPlan → actual events → Review → explicit confirm → ClosedActivityDay`.

| Состояние | Переход | Факт |
|---|---|---|
| Open | Создание сегодня/прошлой даты, индекс старых тренировок | Нет подтверждённого итога |
| Completed | Open → review → checkbox → confirm, минимум одна фактическая запись | Да, frozen summary |
| RestDay | Open → явный отдых → review → checkbox → confirm; в v1 только без событий | Да, явно подтверждённый отдых; не оценка полного суточного расхода |
| MissingData | Только прошлый Open → review → checkbox → confirm | Нет; Closure=null, не нулевая активность |

Review — временный шаг интерфейса. Cancel возвращает в Open без записи итога. Повторное подтверждение отклоняется; второй closure не создаётся. Completed/RestDay/MissingData в этой версии терминальны: reopen/amend не реализован, исправление запрещено. Прямое изменение/удаление/перенос связанных силовых и кардио, а также добавление в закрытую дату, отклоняется общим storage gate. Нельзя обойти его через старый редактор, открытую вкладку или другую дату редактора.

Прошедшее время само по себе не создаёт RestDay или Completed. Пустая прошлая дата отображается как незавершённая; MissingData требует явного выбора. Напоминание о вчера не блокирует приложение. Календарь содержит все даты месяца, включая пустые и будущие; открытие «Активность» выбирает сегодня. Будущие даты показывают пояснение, не создают aggregate, events или closure. Старые future workout records не переписываются и не индексируются до наступления даты; новая запись в будущем запрещена.

Ключ уникальности строже минимального: **ProfileId + local Date**, даже при смене tracking cycle в тот же день. День сохраняет первоначальный TrackingCycleId/AvatarRevision origin и не переносит занятия в новый цикл. Для нового дня выбирается последний cycle, начатый не позже этой даты; для истории до первого lock — самый ранний доступный origin. Это привязка для replay, не утверждение о достоверности старого аватара. Дата `DateOnly` не меняется при timezone switch; timestamps `DateTimeOffset` сохраняют offset записи/закрытия. Границы календаря используют локальные часы устройства, не UTC.

## Источники истины

| Объект | Writable owner | Что хранит ActivityDay |
|---|---|---|
| Состояние дня и plan | ActivityDay envelope | Дата, Profile/cycle/origin, state, plan snapshot, timestamps, schema |
| Силовая | Existing TrainingSession / strength store | Только event ID + стабильный linked session ID |
| Кардио | Existing LoggedWorkout / workouts store | Только event ID + стабильный linked workout ID |
| Ходьба/растяжка/короткое упражнение/заметка | Lightweight ActivityEvent в envelope дня | Введённые параметры и происхождение |
| Подтверждённый итог | ClosedActivityDay в envelope дня | Frozen normalized event summaries, totals, coverage, warnings, adherence; не редактирует журналы |
| План по умолчанию | Тот же envelope | Шаблон для новых дней; изменения не меняют прежние планы |

Индексирование вызывается при открытии активности и изменении исходных коллекций. Dated records материализуют Open days; **ни один исторический день не становится Completed автоматически**. Для силовой в actual учитываются только сессии с выполненными подходами; незавершённые наборы остаются в исходном журнале. Повторный индекс сохраняет day/event IDs и bytes при неизменных данных. Перенос/удаление в открытом журнале обновляет ссылки; старые stores не мигрируются и не копируются. Один source ID может встречаться в индексе только один раз. Ручная запись той же прогулки вторично семантически не распознаётся; форма явно просит не повторять уже внесённое кардио.

## Расчёты и полнота

- Ходьба: distance km или steps, optional minutes. Шаги не превращаются в выдуманную дистанцию. Для distance + minutes используется existing EnergyCalculator, Outdoor Walking/Asphalt/без набора высоты, без новых коэффициентов. В событии freeze context (sex/age/height/weight), model version `energy-calculator-acsm-1`, estimate. Контекст — текущие параметры профиля/аватара, которые могут включать legacy estimates; это оценка, не прямое измерение расхода. Нет времени/дистанции → kcal=null. Изменение параметров тела не пересчитывает историю.
- Cardio: сохранённые duration/distance/active kcal исходного журнала. Strength: только выполненные sets/reps/external volume; duration если введена. Расход силовой неизвестен, не 0.
- Мapped spontaneous: каталог + sets/reps → existing MuscleLoadEngine (его default RIR 2, приблизительная двусторонняя нагрузка). Произвольная растяжка/заметка без mapping не даёт выдуманной мышечной нагрузки. Кардио/ходьба не раскрашивают мышцы.
- Aggregate суммирует raw мышечную экспозицию и L/R до нормализации. Heatmap — относительная карта упражнений, не ЭМГ, не fatigue, не рост.
- Totals по времени включают только известные durations. Nullable km/steps/kcal, counts energy-known events, AllDurationsKnown/AllEnergyKnown и warnings не позволяют интерпретировать отсутствие данных как полный ноль. RestDay с пустым списком означает отсутствие записанной физической активности; полный суточный расход не оценивается. MissingData вообще не имеет factual summary.
- Plan adherence: per-category known actual vs target (km/steps/minutes либо done/not recorded), без общего score. Unplanned types сохраняются отдельно. Питание, intake, deficit и total energy balance отсутствуют.

Closure schema 1 / `activity-summary-1`: day/date/Profile/cycle/origin, offset timestamp, explicit confirmation/outcome; stable event IDs; frozen normalized summaries; category minutes, walking km/steps, cardio distance, strength sets/reps/volume, raw/region muscle load, estimated active kcal, coverage, warnings, structured adherence и unplanned types. Для фактического потребления проверять `IsFactual` и `Closure`, не просто наличие дня или событий.

## Storage, CAS и backup

`workoutcalc.activityDays.v1`: schema-1 SHA-256 envelope с одним payload (`DefaultPlan`, `Days`, `journals-reference-1`). Strict read, required constructor fields, enum/finite/range/uniqueness validators, replay validation frozen totals, corruption/future schema → сохраняется оригинал, запись блокируется. Checksum выявляет повреждение, не является подписью или защитой от пользователя с DevTools.

Один CAS commit записывает day + events + closure. Review захватывает raw activity/cardio/strength/avatar snapshots; confirm сравнивает все зависимости в синхронном browser storage task. Любое изменение после review требует reload/review. Existing localStorage CAS pattern не является multi-device database transaction; приложение local-first, без сервера/синхронизации. Strength writers и cardio writers проверяют закрытые dates/IDs перед каждой мутацией. Кардиожурнал теперь также отклоняет устаревший loaded snapshot.

Full backup автоматически включает весь `workoutcalc.*`, значит новый envelope и references входят целиком. Before restore C# проверяет schema, checksum, avatar origins и соответствие closed summaries исходным journals. Global recovery transaction/epoch из предыдущих фаз обеспечивает rollback и блокирует stale tab даже после restore byte-identical данных. Unsupported future schema архивируется как original bytes, но restore в старый reader отклоняется до мутации.

Opt-in validation section «Дни активности и подтверждённые итоги»: pseudonyms `day-N`, state/date, type/source/duration, kcal/muscle/adherence/coverage для closed days. Allowlist исключает raw day/event/session/Profile/cycle IDs, notes, free-text warnings. Даты остаются чувствительными; фото не включаются автоматически.

## Проверка и границы

`ActivityDayTests`, `ActivityDayStorageTests`, `tests/js/activity-day.test.mjs`, `tests/browser/activity-day-smoke.cjs`. Browser report и screenshots — `docs/evidence/activity-day/`; CI сохраняет evidence artifact. Полный прогон включает прежние avatar creation/domain, skeletal, strength, history, forecast, muscle forecast, product, errors и published offline `/trening/`.

UI actions пересчитывают summary при смене даты, envelope или исходных журналов. Ввод в поля ничего не строит; shape builder не вызывается на add/edit/close. Daily heatmap использует existing current mesh и atlas. Browser smoke сравнивает geometry UUID до/после. Timings включают automation/rendering/scroll; это desktop Edge/SwiftShader, не измерение физического телефона. Числа и фактический test matrix — в отчёте.

Не реализованы: reopen/amend audit revisions, wearable/import UI, dedup разных вручную введённых описаний одной активности, калории силовой/растяжки без надёжной модели, time zone database, full movement coverage. Phase 4+: Nutrition/КБЖУ/meal schedule, energy balance, automatic coach, Hypothesis 14/30, CheckIn/outcomes, GeometryWarpRenderer/AI renderer, BodyParts3D, DeltaShape. #19 formulas/benchmark не включены.
