# Forecast v3 R&D — правила повышения точности числового и визуального прогноза

Статус: roadmap / R&D rules.  
Назначение: рабочий план последовательного развития прогнозного контура после текущих personalized forecast и training-aware muscle/body-shape forecast.

Этот документ **не является реализацией**. Он фиксирует границы, архитектурные правила, порядок исследований, критерии качества и условия GO / NO-GO.

---

# 1. Цель этапа

Главная цель Forecast v3 — повысить одновременно:

1. точность числового прогноза веса и состава тела;
2. точность прогнозируемой 3D-формы;
3. анатомическую правдоподобность локальных изменений мышц;
4. визуальную правдоподобность отображения результата на фотографии.

При этом необходимо сохранить разделение ответственности:

~~~text
Физиология и поведение пользователя
                ↓
      numerical forecast engine
                ↓
         target body state
                ↓
       statistical 3D shape
                ↓
       anatomical muscle layer
                ↓
        final future 3D mesh
                ↓
 structural render conditions
 depth / normals / silhouette
                ↓
    generative image renderer
                ↓
 photorealistic visualization
~~~

## Правило 1.1

**Генеративная image-модель никогда не определяет физиологический результат.**

Она не решает:

- сколько пользователь потеряет веса;
- какой будет процент жира;
- насколько уменьшится талия;
- какие мышцы вырастут;
- где должен уйти жир;
- каким должен стать 3D mesh.

Эти значения обязаны быть рассчитаны до image generation.

## Правило 1.2

Image renderer получает уже рассчитанную target geometry и выполняет только задачу:

> сохранить человека, позу, одежду, освещение и окружение, но привести видимую форму тела к заданной геометрии.

## Правило 1.3

Любой новый алгоритм Forecast v3 внедряется только если он измеримо улучшает существующий baseline либо закрывает ранее неразрешимую функцию.

---

# 2. Неприкосновенные архитектурные правила

Эти правила действуют для всех последующих этапов.

## 2.1 Facts != Estimates != Forecasts != Renders

В системе остаются четыре принципиально разные сущности.

### Facts

Реально введённые или измеренные данные:

- вес;
- ручные обхваты;
- подтверждённый процент жира;
- тренировочный журнал;
- BodySnapshot;
- photo-derived значения с явным provenance.

### Estimates

Вспомогательные значения, необходимые для восстановления текущей формы:

- ANSUR fallback;
- visual completion;
- оценочный % жира;
- восстановленные отсутствующие пропорции.

Они не становятся фактами автоматически.

### Forecasts

Будущие значения модели:

- вес;
- fat/lean;
- вода;
- обхваты;
- regional adaptation;
- shape latent;
- muscle morph state.

### Renders

Изображения, созданные для визуализации ForecastSnapshot.

Render не является новым фактом и не может участвовать в calibration.

---

## 2.2 BodySnapshot immutable по смыслу

Прогноз не имеет права менять historical BodySnapshot.

Новый факт создаётся только:

- пользователем;
- импортом допустимого источника;
- отдельным измерительным pipeline с явным provenance.

---

## 2.3 Visual estimate не становится измерением

Если для построения mesh система использовала оценочную грудь, бедро или иной параметр, в storage он остаётся отсутствующим фактом.

---

## 2.4 ForecastSnapshot остаётся versioned и immutable

Каждый прогноз обязан сохранять:

- model version;
- model parameters;
- входные данные;
- calibration revision;
- shape-model version;
- muscle-model version;
- uncertainty version;
- renderer target version при наличии;
- рассчитанные числовые точки.

Обновление Forecast v3 не должно изменять replay ранее сохранённых прогнозов.

---

## 2.5 Model versioning обязательно

Любой новый слой получает отдельную версию.

Пример:

~~~text
composition: hall-forbes-2
personalization: residual-2
shape: deltashape-1
muscle-geometry: bodyparts3d-field-1
renderer-target: geometry-condition-1
~~~

Версии не должны определяться только номером приложения.

---

## 2.6 Никакого look-ahead leakage

Ни:

- calibration;
- DeltaShape training;
- backtest;
- muscle-response fitting;
- uncertainty fitting

не могут использовать данные, которых не существовало на дату прогнозного origin.

---

## 2.7 Baseline обязателен

Для каждого нового R&D слоя должен существовать baseline.

Примеры:

- improved composition vs текущий ForecastEngine;
- anatomical morphs vs текущие procedural MuscleMorphFields;
- DeltaShape vs текущий BodyShapeForecast;
- conditioned renderer vs обычный image-edit renderer.

Новый подход не принимается только потому, что выглядит сложнее или визуально эффектнее.

---

## 2.8 Synthetic tests != доказательство физиологической точности

Synthetic tests используются для:

- conservation;
- deterministic behavior;
- no-look-ahead;
- serialization;
- geometry stability;
- expected directional semantics.

Они не являются доказательством, что реальный человек изменится именно так.

---

## 2.9 Licensing gate выполняется до production integration

До включения внешнего dataset/model в production необходимо письменно определить:

- разрешено ли коммерческое использование;
- разрешено ли обучение производной модели;
- можно ли распространять derived weights;
- нужны ли attribution / notices;
- есть ли Share-Alike;
- можно ли хранить исходные данные;
- можно ли использовать данные только в research.

Если ответ неизвестен — интеграция считается research-only.

---

# 3. Текущее состояние, которое Forecast v3 улучшает

На момент написания roadmap текущий pipeline имеет следующую структуру:

~~~text
BodyProfile / BodySnapshot
        ↓
Hall/Forbes-inspired ForecastEngine
        ↓
personal residual calibration
        ↓
TrainingStimulusForecast
        ↓
MuscleAdaptationForecast
        ↓
BodyShapeForecast
        ↓
procedural MuscleMorphFields
        ↓
MakeHuman fitting + girth reconciliation
        ↓
future 3D body
~~~

Сильные стороны текущего решения:

- сохранение mass conservation;
- separate water/glycogen;
- adaptive thermogenesis;
- personalized residual;
- immutable forecast versions;
- no-look-ahead backtest;
- regional muscle allocation;
- girth reconciliation;
- deterministic MakeHuman geometry.

Главный долг Forecast v3:

1. некоторые metabolic approximations пока слишком грубые;
2. global future shape строится процедурно;
3. muscle displacement directions заданы вручную;
4. photo visualization не имеет жёсткого geometry conditioning.

---

# 4. Этап A — уточнение weight / composition engine

## Цель

Улучшить текущий ForecastEngine без полного переписывания модели Hall/Forbes.

Основная стратегия:

> заимствовать только те механизмы, которые дают измеримую пользу и имеют доступные пользовательские inputs.

Официальный NIDDK Body Weight Planner основан на работах Kevin Hall и предоставляет подробную динамическую модель изменения веса. Он используется как reference/oracle, а не как production black box.

Reference:

- NIDDK Research Behind the Body Weight Planner:  
  https://www.niddk.nih.gov/research-funding/at-niddk/labs-branches/laboratory-biological-modeling/integrative-physiology-section/research/body-weight-planner
- Body Weight Planner:  
  https://www.niddk.nih.gov/bwp

---

## 4.1 Не заменять текущий engine полностью

Запрещено на первом шаге переносить всю metabolic network Hall:

- lipogenesis;
- gluconeogenesis;
- ketogenesis;
- protein turnover;
- detailed substrate oxidation.

Причина: число неизвестных параметров и inputs станет существенно больше наблюдаемых данных пользователя.

---

## 4.2 Разделить diet energy и activity energy

Текущая модель должна перейти от единого абстрактного balance к явному разделению:

~~~text
DietEnergyChange
ActivityEnergyChange
TotalEnergyBalance
~~~

### Правило

Adaptive thermogenesis не должна одинаково реагировать на:

- -500 kcal от ограничения питания;
- -500 kcal от дополнительной ходьбы/кардио.

Необходимо исследовать и приблизить логику NIDDK/Hall, где metabolic adaptation теснее связана с изменениями intake.

### Fallback

Если нет достаточно данных — сохранять текущий baseline path.

---

## 4.3 Добавить optional macros

ForecastInput v3 может дополнительно содержать:

~~~text
ProteinGramsPerDay?
CarbsGramsPerDay?
FatGramsPerDay?
~~~

Все поля optional.

### Правило

Отсутствие macros не должно блокировать прогноз.

---

## 4.4 Macro-specific TEF

Текущий фиксированный TEF 10% остаётся fallback.

При наличии macros разрешается использовать отдельные приблизительные TEF-компоненты для:

- protein;
- carbohydrate;
- fat.

### Acceptance

На идентичном calorie intake high-protein и high-fat сценарии должны давать небольшое, физически объяснимое различие expenditure, но без чрезмерного эффекта.

---

## 4.5 Glycogen должен учитывать carbohydrate intake

Текущий glycogen/water layer зависит в основном от общего energy balance.

Forecast v3 должен исследовать модель:

~~~text
carb change
→ glycogen target
→ bound water
→ scale weight
~~~

### Правило

При известном carbohydrate intake именно углеводный input должен иметь приоритет перед грубой proxy total-deficit.

### Fallback

Если carbs неизвестны — используется текущая проверенная approximation.

---

## 4.6 Optional sodium / extracellular water

Advanced inputs могут включать:

~~~text
SodiumMgPerDay?
~~~

Это не обязательное поле для обычного пользователя.

### Цель

Улучшить прогноз первых дней/недель при:

- сильном изменении соли;
- рефидах;
- low-carb;
- резких изменениях питания.

### Ограничение

ECF layer не должен превращаться в точный предсказатель ежедневного веса при неизвестном hydration behavior.

---

## 4.7 Не пытаться моделировать весь daily noise

Forecast v3 должен различать:

~~~text
long-term tissue trajectory
short-term modeled water component
unmodeled daily noise
~~~

Unmodeled noise отражается в uncertainty, а не подгоняется каждым новым коэффициентом.

---

# 5. NIDDK / Hall benchmark

До изменения production baseline необходимо создать reference benchmark.

## 5.1 Scenario generator

Сгенерировать набор synthetic profiles по диапазонам:

- sex;
- age;
- height;
- weight;
- body-fat;
- physical activity;
- intake;
- deficit/surplus;
- carb share;
- sodium, если сравнение возможно.

Размер baseline benchmark: не менее нескольких тысяч сценариев.

---

## 5.2 Горизонты

Обязательные точки:

- 1 week;
- 2 weeks;
- 4 weeks;
- 8 weeks;
- 12 weeks;
- 24 weeks.

---

## 5.3 Метрики

Сравнивать как минимум:

- WeightKg;
- weight change;
- fat mass, если доступен reference;
- lean/fat partition;
- early water effect;
- plateau behavior.

---

## 5.4 Stratified analysis

Отдельно анализировать:

- male/female;
- low/normal/high BMI;
- small/large deficit;
- surplus;
- high activity;
- short vs long horizon.

---

## 5.5 GO / NO-GO для composition v2

Новый engine допускается в production, если одновременно:

1. нет серьёзной регрессии на текущих pinned tests;
2. reference benchmark задокументирован;
3. новые механизмы уменьшают систематическое расхождение хотя бы в целевых сценариях;
4. personalized calibration продолжает работать;
5. historical ForecastSnapshot replay не ломается.

---

## 5.6 Затрагиваемые слои

Предположительно:

- ForecastInput;
- ForecastEngine;
- ForecastConstants / model parameters;
- ForecastSnapshot;
- personalized forecast tests;
- backtest docs/UI.

---

## 5.7 Rollback

Старый engine должен оставаться доступен по model version для replay и A/B benchmark.

---

# 6. Этап B — анатомические muscle morphs через BodyParts3D

## Цель

Заменить ручные направления procedural MuscleMorphFields на displacement fields, полученные из реальной анатомической геометрии.

## Основной production candidate

BodyParts3D.

Официальная лицензия на текущий архив: Creative Commons Attribution 4.0 International.

Source:

https://dbarchive.biosciencedbc.jp/en/bodyparts3d/lic.html

Лицензию необходимо повторно проверить на конкретной версии ассета перед включением в production build.

---

## 6.1 Z-Anatomy

Z-Anatomy можно использовать:

- для визуального reference;
- сопоставления названий;
- ручного анатомического аудита.

Не использовать автоматически как production dependency, пока сознательно не принято Share-Alike обязательство.

---

## 6.2 Не заменять MuscleAdaptationForecast

Существующие сущности сохраняются:

~~~text
MuscleAdaptationState
MuscleMorphState
RelativeGrowth
regional allocation
mass caps
~~~

Меняется только geometry mapping:

~~~text
Сейчас:
RelativeGrowth → hand-authored directional field

Forecast v3:
RelativeGrowth → anatomically-derived field
~~~

---

# 7. BodyParts3D import pipeline

## 7.1 Выбрать anatomical objects

Необходимо создать mapping:

~~~text
BodyParts3D object
→ internal muscle group
→ left/right
→ attachment region
~~~

Приоритет v1:

- pectoralis;
- anterior/lateral/posterior deltoid;
- biceps;
- triceps;
- lats;
- trapezius;
- rhomboid area, если доступно;
- gluteus maximus/medius;
- quadriceps;
- hamstrings;
- calves.

Мелкие мышцы допускается агрегировать.

---

## 7.2 Canonical registration

Анатомические meshes нельзя просто положить внутрь MakeHuman по абсолютным координатам.

Нужна регистрация относительно landmark system:

- shoulder;
- elbow;
- wrist;
- hip;
- knee;
- ankle;
- spine;
- pelvis;
- clavicle.

### Правило

Registration должна быть определена через skeleton landmarks, а не вручную под одну геометрию.

---

## 7.3 Muscle-to-skin mapping

Для каждого anatomical muscle нужно построить поле влияния на поверхность тела.

Возможный pipeline:

~~~text
muscle mesh
→ nearest / signed-distance field
→ local thickness direction
→ skin projection
→ smooth spatial envelope
→ joint/tendon fade
→ displacement field
~~~

---

## 7.4 Направление роста

Запрещено ограничиваться новым вариантом:

~~~text
skinNormal * influence
~~~

если это создаёт balloon effect.

Направление должно учитывать:

- поверхность мышцы;
- orientation belly;
- близость к кости;
- локальную поверхность кожи;
- суставные зоны.

---

## 7.5 Tendon / joint fade zones

Displacement должен плавно стремиться к нулю около:

- плечевого сустава;
- локтя;
- запястья;
- тазобедренного;
- колена;
- лодыжки;
- сухожильных концов.

---

## 7.6 Protected regions

Не деформировать muscle layer:

- face;
- scalp;
- hands/fingers;
- feet/toes;
- jaw;
- eyes.

Neck — только при явной группе и строгих ограничениях.

---

## 7.7 Girth reconciliation остаётся главным constraint

Known/manual girths имеют приоритет над muscle artistic appearance.

После применения anatomical morphs:

1. измерить resulting girths;
2. выполнить существующий reconciliation/refit;
3. проверить остаточную ошибку.

Если anatomical layer ухудшает constraint выше установленного tolerance — fallback к safe geometry.

---

## 7.8 Mass conservation

Anatomical geometry не имеет права создать новую physiologic mass.

Количество regional lean delta продолжает приходить только из MuscleAdaptationForecast.

---

## 7.9 Precomputed sidecar

Тяжёлый anatomical preprocessing не выполнять в браузере на каждом запуске.

Предпочтительно создать versioned binary sidecar:

~~~text
makehuman-muscle-fields-v1.bin
~~~

Содержимое:

- topology/source hash;
- anatomy asset version;
- licensing metadata/version ID;
- group mapping;
- displacement vectors;
- fade weights;
- checksum.

---

# 8. Acceptance criteria для anatomical morphs

## 8.1 Identity

Zero MuscleMorphState:

- bitwise identity, если возможно;
- либо geometry difference ниже машинной tolerance.

---

## 8.2 Geometry safety

На полном наборе extreme profiles:

- нет NaN;
- нет Inf;
- нет inverted catastrophic geometry;
- нет крупных self-intersections около суставов;
- нет разрывов.

---

## 8.3 Girth safety

Specified girths не ухудшаются по сравнению с procedural baseline более установленного tolerance.

Начальный ориентир: 0.1 см, если текущий solver позволяет.

---

## 8.4 Anatomical differentiation

Morph fields должны существенно различаться пространственно для:

- pec;
- lat;
- deltoid;
- biceps;
- triceps;
- glute;
- quads;
- hamstrings;
- calves.

---

## 8.5 Visual audit

Обязательные ракурсы:

- front;
- side;
- back;
- 3/4 front;
- 3/4 back.

Проверять:

- +small;
- +medium;
- +max allowed morph;
- negative/detraining where supported.

---

## 8.6 Performance

Не допускается per-frame CPU deformation.

Morph применяется только при:

- смене тела;
- forecast week;
- program;
- model version.

---

## 8.7 Rollback

Procedural fields остаются доступным model version/fallback, пока anatomical v1 не прошёл validation.

---

# 9. Этап C — longitudinal Future Body / DeltaShape

## Цель

Перестать определять global future body исключительно через procedural girth-to-mesh deformation.

Создать статистическую модель того, **как реальная человеческая форма изменяется между двумя временными точками**.

---

# 10. Главный источник — Shape Up! longitudinal

Исследование Shape Up! включает paired baseline/follow-up:

- 3D optical scans;
- DXA;
- 133 participants;
- 45 women;
- 6 intervention studies;
- mean follow-up около 13 недель;
- диапазон 3–23 недели.

Это хорошо совпадает с целевыми горизонтами продукта.

Опубликованные результаты показывают, что изменения 3D body shape содержат информацию об изменениях fat/lean composition.

Source:

https://pmc.ncbi.nlm.nih.gov/articles/PMC10315406/

Данные не публичны; доступ возможен исследователям по запросу и должен быть согласован отдельно.

---

## 10.1 Доступ к данным — отдельная задача

Параллельно с этапами A/B/E необходимо:

1. подготовить описание исследования;
2. сформулировать цель использования;
3. запросить доступ;
4. уточнить:
   - commercial derivative rights;
   - allowed model training;
   - weight redistribution;
   - publication requirements;
   - data retention;
   - privacy controls.

До получения явного разрешения любые эксперименты считать research-only.

---

# 11. Вторичный источник — Fenland

Fenland полезен как большой longitudinal statistical prior.

Опубликованное исследование включает:

- более 12k участников в основной выборке;
- 5733 участников с валидными данными в двух фазах;
- средний longitudinal interval около 6.7 лет;
- DXA-derived composition + 3D body-shape reconstruction.

Source:

https://www.nature.com/articles/s41746-024-01289-0

Data access — по запросу через MRC Epidemiology.

---

## 11.1 Роль Fenland

Fenland не использовать как единственный short-term forecast dataset.

Основное применение:

- большой shape prior;
- composition ↔ shape relation;
- OOD/subgroup analysis;
- long-term body-shape variability.

Shape Up! важнее для 4–12 week DeltaShape.

---

# 12. Не предсказывать 13k вершин напрямую

Запрещённый первый подход:

~~~text
profile + plan → 13,380 XYZ vertices
~~~

Причины:

- high-dimensional output;
- плохая data efficiency;
- риск нестабильной геометрии;
- сложно контролировать constraints.

---

# 13. Registered topology

Все scans для DeltaShape R&D должны быть приведены к:

- единой topology;
- canonical pose;
- единым landmarks;
- стабильному vertex correspondence.

Если dataset использует другую topology, необходим explicit registration layer.

---

# 14. Latent statistical shape

Предпочтительный подход:

~~~text
registered mesh
→ PCA / learned compact latent
→ z1...zN
~~~

Необходимо определить минимальный latent dimension, который объясняет большую часть meaningful shape variance без overfit.

---

# 15. DeltaShape formulation

Модель должна предсказывать не абсолютное тело из нуля, а изменение относительно известного человека.

Входы-кандидаты:

~~~text
start_shape_latent
sex
age
height
start_weight
start_body_fat
delta_fat
delta_lean
regional_muscle_delta
horizon_days
training_category / stimulus summary
~~~

Выход:

~~~text
delta_shape_latent
~~~

И затем:

~~~text
z_future = z_start + delta_z
~~~

---

# 16. Global shape и muscle anatomy — разные слои

DeltaShape отвечает за:

- живот;
- torso fullness;
- hips;
- thigh fullness;
- general fat distribution;
- broad lean/fat-associated form change.

Anatomical muscle layer отвечает за:

- локальную форму pec;
- delts;
- biceps;
- triceps;
- lats;
- glutes;
- quads;
- hamstrings;
- calves.

Нельзя обучать их как один непрозрачный black box до появления достаточного dataset.

---

# 17. Reconciliation после DeltaShape

Результат DeltaShape обязан пройти:

- height consistency;
- volume sanity;
- predicted girth consistency;
- composition constraints;
- muscle mass allocation constraints.

### Правило

Статистическая модель формы не имеет права изменить числовой forecast молча.

---

# 18. DeltaShape train / validation правила

## 18.1 Split только по participant

Никогда не делить scan pairs случайно по строкам.

Все scans одного человека находятся только в одном split:

- train;
- validation;
- test.

---

## 18.2 No identity leakage

Если у человека несколько intervention points, они остаются в одном participant split.

---

## 18.3 Horizon evaluation

Отдельно оценивать хотя бы:

- short: <= 6 weeks;
- medium: 7–12 weeks;
- long: > 12 weeks.

Если данных хватает — отчёты 4 / 8 / 12 недель.

---

## 18.4 Метрики

Обязательные:

- vertex-to-vertex error;
- surface distance;
- silhouette IoU / contour distance;
- waist error;
- chest error;
- hips error;
- thigh error;
- arm error;
- regional volume error;
- total volume error.

---

## 18.5 Baseline

Сравнивать с текущим:

~~~text
BodyShapeForecast + MakeHuman fitting
~~~

DeltaShape считается полезным только если улучшает held-out participants.

---

## 18.6 Subgroups

Отдельно:

- sex;
- BMI categories;
- age bands;
- fat-loss vs gain;
- strength intervention vs no-strength, если labels доступны.

---

## 18.7 OOD detection

Если input сильно выходит за training distribution, system должна:

- расширять uncertainty;
- либо fallback к procedural model;
- не выдавать statistical model как высокоуверенный прогноз.

---

# 19. GO / NO-GO для DeltaShape

Production integration разрешена только если:

1. dataset license/rights разрешают intended use;
2. participant leakage исключён;
3. DeltaShape beats procedural baseline на independent test;
4. improvements есть не только на global vertex metric, но и на ключевых girths;
5. OOD behavior определён;
6. current factual mesh не ухудшается;
7. fallback path сохранён.

---

# 20. Этап D — Pseudo-DXA как auxiliary constraint

## Цель

Исследовать возможность восстанавливать дополнительные priors regional fat/lean из 3D shape.

Pseudo-DXA не является обязательным dependency Forecast v3.

---

## 20.1 Запрещено

Нельзя называть output Pseudo-DXA:

- фактическим DXA;
- измеренной мышечной массой;
- медицинским результатом.

---

## 20.2 Возможные применения

Только если validation подтверждает пользу:

~~~text
current surface
→ estimated regional composition prior
→ compare with ForecastConstants regional assumptions
~~~

или:

~~~text
future surface
→ consistency check
→ detect impossible regional allocation
~~~

---

## 20.3 GO criterion

Интегрировать только если auxiliary constraint измеримо:

- снижает regional composition error;
- либо улучшает future-shape validation.

Иначе оставить research experiment.

---

# 21. Этап E — geometry-conditioned photorealistic renderer

## Цель

Создавать реалистичное изображение forecast result, не передавая generative model право решать форму тела.

---

# 22. Odo — architectural reference

Odo: Depth-Guided Diffusion for Identity-Preserving Body Reshaping показывает подход:

~~~text
source image
+
target 3D depth
+
identity/reference conditioning
→ reshaped person
~~~

Reference:

https://github.com/FastCodeAI/Odo

Лицензия Odo: CC BY-NC-SA 4.0, non-commercial research.

### Правило

Не копировать Odo production code/weights как коммерческую dependency.

Использовать:

- архитектурную идею;
- benchmark design;
- structural-conditioning principles.

---

# 23. Prototype renderer candidate — Qwen Image Edit

Qwen-Image-Edit-2509 можно использовать как один из prototype candidates.

На момент roadmap model card указывает Apache 2.0.

Source:

https://huggingface.co/Qwen/Qwen-Image-Edit-2509

Перед production deployment необходимо повторно проверить:

- exact checkpoint license;
- adapters/control modules;
- inference-provider terms;
- privacy/data retention.

---

# 24. Renderer pipeline

## 24.1 Исходные данные

Пользователь выбирает реальное исходное фото.

---

## 24.2 Camera / pose alignment

Нужно оценить или использовать известные:

- camera intrinsics approximation;
- body pose;
- skeleton keypoints;
- segmentation.

---

## 24.3 Target geometry

ForecastSnapshot определяет точную future 3D geometry.

---

## 24.4 Structural maps

Из target mesh рендерятся:

- depth map;
- normal map;
- silhouette/body mask;
- skeleton/keypoints;
- optional segmentation by body region.

---

## 24.5 Image model input

Image model получает:

1. исходное фото;
2. target structural conditions;
3. минимальную инструкцию на сохранение identity/scene.

---

## 24.6 Запрещённый prompt

Не использовать как единственный control:

> сделай человека худее на 8 кг

или:

> сделай более мускулистым.

Допустим только вспомогательный текст вокруг уже заданной geometry.

---

# 25. Renderer invariants

Renderer должен стремиться сохранить:

- face identity;
- hair;
- pose;
- camera;
- clothes identity;
- lighting;
- background;
- skin identity.

Изменяться должна прежде всего body geometry согласно target.

---

# 26. Render не участвует в calibration

Сгенерированная картинка никогда не становится:

- BodySnapshot;
- фактом;
- measurement source;
- training label для user calibration.

---

# 27. Render provenance

Каждый generated result должен быть связан с:

- source photo ID;
- ForecastSnapshot ID;
- target mesh/model version;
- structural maps version;
- renderer model/checkpoint;
- renderer settings;
- creation timestamp.

---

# 28. Privacy

Photorealistic renderer может потребовать server/GPU inference.

Перед upload фотографии:

- явное согласие пользователя;
- provider disclosure;
- retention policy;
- delete policy.

Local-only приложение обязано продолжать работать без photorealistic renderer.

---

# 29. Renderer validation

Нельзя оценивать renderer только «красиво / некрасиво».

Обязательные метрики:

## 29.1 Target adherence

Generated image повторно прогоняется через independent body/silhouette estimator.

Сравнивать proxy geometry с target mesh:

- silhouette;
- widths;
- key girths where recoverable.

---

## 29.2 Identity preservation

Использовать отдельную identity similarity metric или controlled manual audit.

---

## 29.3 Pose consistency

Keypoint displacement должен оставаться небольшим, если target pose не менялся.

---

## 29.4 Scene consistency

Проверять:

- background;
- clothes;
- camera crop;
- object preservation.

---

## 29.5 Human audit

Проверять:

- distorted fabric;
- duplicated limbs;
- deformed hands;
- implausible shadows;
- body/background warping.

---

# 30. GO / NO-GO renderer

Renderer считается пригодным для пользовательского preview, если:

1. structural adherence лучше обычного text/image-edit baseline;
2. identity достаточно стабильна;
3. нет систематического «улучшения» physique сверх target;
4. output clearly labelled как visualization;
5. privacy flow реализован;
6. renderer можно полностью отключить без потери core forecast.

---

# 31. Что не делать на этом этапе

## 31.1 Не мигрировать MakeHuman → SMPL-X

Текущий MakeHuman pipeline уже имеет:

- fitted measurements;
- skeleton;
- animation;
- Muscle Atlas;
- morphology;
- history;
- constraints.

Полный rewrite сейчас имеет плохое отношение риск/польза.

---

## 31.2 Не делать SHAPY production dependency

SHAPY/SMPL-family можно использовать для research comparison или registration experiments.

Production dependency откладывается до отдельного решения по лицензии и measured benefit.

---

## 31.3 Не внедрять BodyM первым

BodyM полезен для image→measurement estimation, но это не текущий главный bottleneck.

Вернуться к BodyM, если manual validation покажет, что текущий initial-body/photo pipeline даёт слишком большую ошибку.

---

## 31.4 Не использовать image generation как forecast engine

Ни одна diffusion/image-edit model не должна решать, как распределяется изменение тела.

---

## 31.5 Не обещать muscle mass из фото

Girth и visual shape не могут самостоятельно идентифицировать настоящую массу отдельной мышцы.

---

## 31.6 Не строить full metabolic digital twin без данных

Сложность модели не является целью.

Добавлять только параметры, которые:

- можно наблюдать;
- можно валидировать;
- улучшают forecast.

---

# 32. Целевая архитектура Forecast v3

~~~text
             CURRENT FACTUAL BODY
 BodySnapshot + manual measurements + photos
                        ↓
              CURRENT CANONICAL MESH

================================================

             NUMERICAL FORECAST

       improved Hall / Forbes engine
         + diet/activity separation
         + optional macro-specific TEF
         + carb-driven glycogen
         + optional sodium / ECF
         + personalized residual model
                        ↓
       future fat / lean / water states

================================================

             GLOBAL SHAPE MODEL

             current shape latent
                    +
            predicted composition
                    +
          longitudinal shape priors
                    ↓
              DeltaShape model
                    ↓
            future global surface

================================================

            LOCAL MUSCLE MODEL

              strength program
                    ↓
              MuscleLoadEngine
                    ↓
          MuscleAdaptationForecast
                    ↓
       BodyParts3D anatomical fields

================================================

 future global shape + anatomical local layer
                    ↓
           girth / volume reconciliation
                    ↓
            FINAL FUTURE 3D MESH

================================================

              PHOTO VISUALIZATION

            original user photo
                    +
        target depth / normals / mask
                    +
                keypoints
                    ↓
        geometry-conditioned renderer
                    ↓
      PHOTOREALISTIC FORECAST VISUAL

================================================

 REAL BodySnapshots / outcomes
             ↓
 evaluation / backtest / calibration
             ↓
 future model revisions
~~~

---

# 33. Порядок реализации

Порядок обязателен, если независимый результат предыдущего этапа не показывает необходимость перестановки.

## Шаг 1 — Hall/NIDDK refinement

### Входы

- текущий ForecastEngine;
- NIDDK/Hall equations/reference;
- current saved forecast tests.

### Выходы

- benchmark harness;
- diet/activity separation;
- optional macros;
- improved glycogen layer;
- optional sodium/ECF prototype.

### Acceptance

- benchmark documented;
- no regression historical replay;
- measurable improvement/reference agreement.

### Rollback

- старый model version.

---

## Шаг 2 — BodyParts3D anatomical fields

### Входы

- BodyParts3D assets;
- MakeHuman skeleton;
- Muscle Atlas;
- current MuscleMorphState.

### Выходы

- importer;
- canonical registration;
- precomputed displacement sidecar;
- anatomical morph model version.

### Acceptance

- geometry/girth safety;
- deterministic;
- visual anatomy audit;
- performance acceptable.

### Rollback

- procedural morph fields.

---

## Шаг 3 — geometry-conditioned renderer proof-of-concept

### Входы

- source user photo;
- current/future mesh;
- depth/normal/mask renderer.

### Выходы

- renderer adapter interface;
- one research implementation;
- structural adherence benchmark.

### Acceptance

- generated silhouette closer to target than unconditioned baseline;
- identity preserved acceptably;
- renderer never feeds calibration.

### Rollback

- 3D forecast only.

---

## Шаг 4 — data-access requests

Выполняется параллельно.

### Shape Up!

Подготовить application на longitudinal scans + DXA.

### Fenland

Подготовить request через MRC data sharing.

### Acceptance

До получения данных документировать:

- request status;
- allowed use;
- commercial implications.

---

## Шаг 5 — DeltaShape R&D

### Входы

- longitudinal registered meshes;
- composition changes;
- current procedural predictions.

### Выходы

- registered representation;
- latent model;
- participant-safe split;
- DeltaShape predictor;
- benchmark.

### Acceptance

- held-out improvement over procedural baseline;
- no leakage;
- subgroup report.

### Rollback

- current procedural BodyShapeForecast.

---

## Шаг 6 — Pseudo-DXA constraint research

Только после DeltaShape prototype.

### Acceptance

Внедрять лишь при measurable incremental gain.

---

## Шаг 7 — production hardening

Только после успешной validation.

Включает:

- model package/versioning;
- asset licensing;
- observability;
- storage migration;
- privacy;
- fallback;
- performance;
- user-facing uncertainty.

---

# 34. Data / licensing checklist

Перед каждым внешним dataset/model ответить письменно.

- [ ] Точная версия источника.
- [ ] License URL.
- [ ] Commercial use allowed?
- [ ] Training derivative model allowed?
- [ ] Derived weights commercial use allowed?
- [ ] Redistribution allowed?
- [ ] Attribution required?
- [ ] Share-Alike?
- [ ] Raw data retention restrictions?
- [ ] Privacy/security requirements?
- [ ] Geographic restrictions?
- [ ] Publication/review requirement?
- [ ] Can model weights leave research environment?
- [ ] Can production inference depend on this source?

---

# 35. Текущий licensing ориентир

Эти записи являются roadmap guidance и должны проверяться повторно при реализации.

## BodyParts3D

Production candidate.

На текущем официальном LSDB archive указана CC BY 4.0.

https://dbarchive.biosciencedbc.jp/en/bodyparts3d/lic.html

---

## Z-Anatomy

Reference candidate.

Использовать только после отдельного анализа Share-Alike последствий.

---

## Odo

Research architecture reference.

Repository указывает CC BY-NC-SA 4.0 / non-commercial research.

https://github.com/FastCodeAI/Odo

---

## Shape Up!

Restricted research data.

Нужен application и согласование intended use.

https://pmc.ncbi.nlm.nih.gov/articles/PMC10315406/

---

## Fenland

Data available by request through MRC Epidemiology.

https://www.nature.com/articles/s41746-024-01289-0

---

## Qwen-Image-Edit candidate

Model card на момент roadmap указывает Apache 2.0.

https://huggingface.co/Qwen/Qwen-Image-Edit-2509

Проверять точный checkpoint и все дополнительные ControlNet/adapters отдельно.

---

# 36. Общая validation strategy

Forecast v3 должен иметь четыре независимых уровня validation.

---

## 36.1 Numerical composition validation

Проверяет:

- weight;
- fat;
- lean;
- water.

Источники:

- reference models;
- prospective real user history;
- controlled datasets where available.

---

## 36.2 Current-body shape validation

Проверяет:

- реальные ручные girths;
- mesh-computed girths;
- silhouettes;
- photos;
- volume.

---

## 36.3 Future-shape validation

Самая важная проверка Forecast v3.

Для заранее сохранённого forecast:

~~~text
T0 factual body
→ predicted mesh T1

T1 real body
→ reconstructed factual mesh

predicted vs factual
~~~

Метрики:

- girths;
- silhouette;
- surface distance;
- regional shape.

---

## 36.4 Renderer validation

Проверяет только:

> насколько изображение соответствует target future mesh.

Не проверяет физиологию.

---

# 37. Real-user longitudinal protocol

До коммерческих заявлений необходимо накопить prospective данные.

Минимальный пользовательский цикл:

1. baseline manual measurements;
2. baseline front/side/back photos;
3. weight trend;
4. training program;
5. nutrition plan/actuals;
6. saved ForecastSnapshot before outcome;
7. follow-up через 4/8/12 weeks;
8. повторные measurements/photos;
9. factual BodySnapshot;
10. automatic prediction error report.

### Правило

Ретроспективно построенный прогноз не считается доказательством accuracy.

---

# 38. Метрики проекта

## Weight

- MAE;
- bias;
- horizon-specific error;
- range coverage.

## Girths

- MAE per girth;
- signed bias;
- source/manual vs photo stratification.

## Future mesh

- mean surface error;
- percentile surface error;
- silhouette error;
- regional errors.

## Muscle layer

Не измерять «точность кг мышцы», если ground truth отсутствует.

Оценивать:

- anatomical localization;
- shape direction;
- girth consistency;
- relevant longitudinal proxies.

## Renderer

- structural target adherence;
- identity;
- pose;
- scene consistency.

---

# 39. GO / NO-GO перед production Forecast v3

Forecast v3 не считается готовым только потому, что все unit tests зелёные.

Необходимы следующие gates:

- [ ] Weight/composition benchmark задокументирован.
- [ ] Improved weight engine не ухудшает baseline критично.
- [ ] Anatomical morphs проходят geometry/girth constraints.
- [ ] BodyParts3D licensing оформлен.
- [ ] Renderer имеет structural benchmark.
- [ ] Renderer model/license разрешают intended deployment.
- [ ] DeltaShape trained with participant-level split.
- [ ] Нет participant leakage.
- [ ] DeltaShape beats procedural shape baseline.
- [ ] OOD/fallback behavior определён.
- [ ] Historical forecasts replay без изменений.
- [ ] Model versions pinned.
- [ ] Full CI green.
- [ ] Prospective user-validation protocol работает.
- [ ] Synthetic tests не используются как marketing accuracy evidence.

---

# 40. Правила Git и разработки

## 40.1 Этот документ — roadmap

Коммит документа не должен менять production code.

---

## 40.2 Каждый крупный этап — отдельный PR

Рекомендуемые ветки:

~~~text
rnd/composition-v3
rnd/anatomical-muscle-fields
rnd/geometry-renderer
rnd/deltashape
rnd/pseudo-dxa
~~~

После R&D допускаются отдельные production PR.

---

## 40.3 Research code не смешивать с production без gate

Experimental notebooks/tools/datasets должны быть отделены от runtime приложения.

---

## 40.4 External assets не коммитить без license decision

Особенно:

- restricted scans;
- patient-derived data;
- large model weights;
- non-commercial weights.

---

## 40.5 Документировать отрицательный результат

Если технология не дала improvement, это фиксируется в docs/R&D, а не удаляется бесследно.

Это предотвращает повторное исследование неудачного подхода.

---

# 41. Первый набор конкретных задач после этого roadmap

После завершения product consolidation и ручной пользовательской проверки выполнять в следующем порядке:

1. Создать NIDDK/Hall benchmark harness.
2. Проверить и исправить diet-vs-activity adaptive thermogenesis.
3. Добавить optional macro inputs и macro-aware TEF.
4. Сделать carb-driven glycogen experiment.
5. Сделать optional sodium/ECF experiment.
6. Скачать и проаудировать конкретный BodyParts3D release.
7. Создать mapping BodyParts3D → 20 internal muscle groups.
8. Построить offline anatomy registration prototype.
9. Сгенерировать first anatomical displacement sidecar.
10. Провести side-by-side visual/geometry benchmark procedural vs anatomical.
11. Добавить export target depth/normal/silhouette из predicted MakeHuman mesh.
12. Создать renderer interface и unconditioned baseline.
13. Создать depth/structure-conditioned renderer prototype.
14. Сравнить structural adherence.
15. Параллельно подать заявки Shape Up! / Fenland.
16. После доступа построить registered longitudinal research dataset.
17. Создать procedural baseline benchmark на тех же participants.
18. Обучить first DeltaShape latent model.
19. Провести participant-held-out evaluation.
20. Только после этого решать production integration.

---

# 42. Итоговый принцип

Forecast v3 должен двигаться от:

~~~text
правдоподобная эвристика
~~~

к:

~~~text
измеримый числовой прогноз
+
статистически обоснованная форма
+
анатомически правдоподобные локальные изменения
+
фотореалистичный, но геометрически управляемый рендер
~~~

Самое важное правило всей дальнейшей разработки:

> **Мы не улучшаем картинку ценой потери проверяемости прогноза.**

Каждый слой должен оставаться отделимым, versioned, воспроизводимым и измеримым.
