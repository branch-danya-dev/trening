# Training-aware muscle / body-shape forecast v1

**Это модель относительной адаптации и формы, не медицинская оценка и не измерение реальной мышечной массы.**
There is no ML, physiological identification, EMG inference, backend, account or synchronization.
The exercise heatmap is relative exposure. A girth contains fat, skin, water and other tissues;
it cannot identify growth of one muscle. Numerical kg below are a conserved allocation budget
inside an illustrative composition model, not measured muscle kg.

## Architecture and compatibility

The stacked change builds on PR #15, `f493cf12417e21c7b13991e3e7318f4bc5927680`.
PR #10 is outside this change. No automatic merge.

The expected projection is:

```
ForecastEngine baseline composition
  -> ForecastPersonalization (existing weight / partition calibration)
  -> TrainingStimulusForecast (past completed / future planned)
  -> MuscleAdaptationForecast (20 groups, weekly states, conserved allocation)
  -> BodyShapeForecast (girth redistribution + heuristic ranges)
  -> MakeHuman rest-shape fields / reconciliation / runtime rig
```

`ForecastInput.StrengthProgram` is optional. Null preserves the old composition and local
shape behavior. `StrengthTraining=false` disables the new layer. A program supplies its total
session frequency to the existing expenditure calculation; it does not introduce another calorie
estimator. Equal session frequency and energy inputs give identical composition points regardless
of exercise selection. Existing global calibration is unchanged for weight/body fat. Girth facts
from regional forecasts go to the separate shape calibration, so the same effect is not learned
again as a global girth multiplier.

`StrengthProgram` contains immutable recurring sessions and ordered exercises with the existing
`TrainingSet` values. The small editor reuses `StrengthExerciseDraft` / `StrengthSetDraft`, catalog
and validation. It supports exercises, individual sets, repetitions, external kg, RIR or RPE and
frequency. Domains allow optional day offsets, but v1 has weekly resolution: within-week order,
days, supersets, progression and deloads have no modeled effect. Limit: 14 sessions/week,
20 exercises/session, 20 sets/exercise. Empty editor means no detailed program.

## Inputs and temporal boundary

`TrainingSession -> PerformedExercise -> TrainingSet -> StrengthAggregation -> MuscleLoadEngine`
is the single exposure path. Actual sessions count only completed sets. Future program sets count
regardless of their `Completed` flag. Neither path sums normalized heatmap colors.

History seeds adaptation/fatigue over the 12 seven-day bins immediately before forecast start.
Sessions on or after start are excluded. Facts/sessions later than the issue date are also excluded
when issuing a forecast for a future start. Empty history is unknown adherence, not evidence of
perfect adherence. Historical training does not retroactively invent measured starting muscle mass:
week zero always has zero local mass delta and an identity morph.

The journal does not have trustworthy per-entry creation timestamps. The archive freezes the
then-supplied aggregate evidence, cutoff and response factors at issue time. Backtests only replay
these saved forecasts; they cannot retrospectively reconstruct what a past user knew from today's
edited journal. This is a deliberate limit of local data, not a claim of causal identification.

## Stimulus formula

For a completed/planned set with role weight `r`:

```
raw contribution = r / 3 * sqrt(clamp(reps, 1, 30) / 10)
                   * max(0.2, 1 + (2 - reserve) * 0.1)
reserve = RIR, or 10 - RPE, or default 2
r = 1 primary, 0.5 secondary, 0.2 stabilizer
weekly S_g = sum(raw_g) / (4 + sum(raw_g))
```

Thus 12 primary sets of 10 reps at RIR2 give `S=0.5`. Arbitrarily many sets approach 1 and do
not create arbitrarily large growth. The established load engine can scale by an explicit same-
exercise reference weight. The journal/program supplies no reliable personal reference in v1, so
external kg are retained but not treated as comparable intensity across squat, curl and bench.
This avoids interpreting a heavier bar as automatically greater relative stimulus.

## Adaptation, recovery, experience and energy

All coefficients are transparent v1 heuristics, not fitted physiological constants. For every group:

```
E = S / (1 + 0.6 * previousFatigue)
A_next = clamp(A * (1 - 0.04 * (1 - S)) + rate * E * (1 - A), 0, 1)
F_next = 0.35 * previousFatigue + 0.65 * S
rate = 0.18 beginner, 0.12 intermediate, 0.08 advanced
weekly capacity = max_g(E_g * (1 - 0.5 * A_g))
energy gate = clamp(previousWeekBalanceKcal / 250, 0, 1)
potential += originalWeeklyLeanCeiling(sex, experience, previousWeight)
             * energy gate * weekly capacity
```

With no stimulus, fatigue recovers (×0.35/week) and adaptation decays (×0.96/week).
Refractory fatigue and existing adaptation reduce subsequent potential. Experience acts through
both adaptation speed and the existing lean ceiling. The existing female ceiling factor is retained.
No new positive allocation is earned by a deficit. Starting exposure is not a sex-dependent mass
measurement. These constraints operate *after* personalization, without altering its weight,
fat, water or lean numbers.

## Conserved regional allocation

Stable group order and bilateral IDs come from the existing MuscleDefinitions (20 groups,
39 anatomical regions plus neutral index zero). Prior weights, normalized by their sum:

| Group | Weight | Group | Weight |
|---|---:|---|---:|
| pectoralis | 8 | anterior / lateral / posterior deltoid | 2 each |
| lats | 8 | traps / rhomboids | 4 / 3 |
| biceps / triceps / forearms | 3 / 4 / 2 | rectus / obliques / erectors | 3 / 3 / 5 |
| glute-max / glute-med | 10 / 3 | quadriceps / hamstrings | 16 / 9 |
| adductors / hip-flexors / calves | 5 / 2 / 6 | | |

The baseline local budget is `0.45 * startingLean * prior_g`. This is an allocation prior, not
an estimate of each muscle's actual mass. For cumulative composition change `netLean`:

* Positive: requested allocation = `min(netLean, accumulatedPotential)`, scores =
  `prior_g * (0.2 + 4 * E_g * (1 - 0.5 * A_g) * response_g)`.
* Negative: requested allocation = `0.45 * netLean`, scores = `prior_g / (1 + 2 * E_g)`.
  Every group has a nonpositive delta; loaded groups lose a smaller fraction of their prior.
* Capped water filling preserves the available requested budget exactly, unless the sum of
  capacities is smaller. Unallocated lean stays in the original composition model.
* Each group gets at most 22% of requested absolute allocation. Positive deltas also have a cap
  of 25% of their starting local budget; losses have a cap of 90%, leaving positive modeled mass.
* The 0.2 prior floor avoids erasing untrained regions. Left/right use the same group value;
  bilateral kg are counted once, not once per side.

Tested invariants at every week:

```
sum(group LeanDeltaKg) == AllocatedLeanKg
sum(max(0, group LeanDeltaKg)) <= max(0, compositionLean - startingLean)
startingLocalBudget + LeanDeltaKg > 0
```

`RelativeGrowth = clamp(LeanDeltaKg / startingLocalBudget, -0.25, 0.25)` is the documented
dimensionless form state. This is what the UI reports as a percentage of an index.

## Girth projection

Chest includes pectoralis, deltoids, lats and rhomboids; arms include biceps/triceps/forearms;
hips include glutes; thighs include quadriceps/hamstrings/adductors/hip-flexors/calves;
traps map to neck, trunk stabilizers to waist. Small groups can therefore share a visual region.

For each of the existing composition regions, replace only the allocated fraction of the
default lean distribution: `correction = sum(localDeltas) - AllocatedLean * oldLeanShare`.
Use existing tissue density and GirthSensitivity:
`C_new = sqrt(C_composition² + 4π * correctionVolume / effectiveLength)`.
The old 5% "other" share becomes modeled regions in this allocated fraction; the total budget
is unchanged. Calf follows the thigh girth ratio in v1, while its local surface field is independent.
Fat distribution, unallocated lean and composition calibration remain in their original layer.
The wrist has no local muscle correction.

## MakeHuman morph pipeline and reconciliation

The actual pipeline deliberately fits **after** inserting the local field:

1. MakeHuman macros/form targets and scale to profile height (cached rest coordinates).
2. Atlas-based directional muscle fields in rest coordinates.
3. Existing normal-based *soft tissue* layer and warm girth targets.
4. Personal posture transfer; girths and volume are measured in that posture.
5. Final girth fitting and volume correction, each volume step re-fits girths.
6. Bind the resulting personal rest geometry; existing skeletal runtime animation follows it.

The local muscle field is not `normal * heatmap`: canonical directions favor front projection
for pectoralis/biceps/quads, posterior projection for triceps/hamstrings/calves/glutes, lateral+
posterior projection for lats and shoulder-specific directions for deltoids. Upper-arm vectors
are projected perpendicular to the arm axis. Smooth proximal/distal envelopes and joint-distance
fades suppress shoulder/elbow/hip/knee/ankle artifacts. Any vertex influenced by head, neck,
hand/finger/thumb, foot/toe, jaw or eye bones is excluded from the local field. Rectus, obliques,
erectors, forearms, adductors and hip-flexors affect allocation/girths, with no independent v1 lobe.
These fields are procedural curated targets; artist-authored targets are not bundled.

Maximum direct displacement is 8 mm at index magnitude 0.25 and height 175 cm, scaled by height;
atlas influence weights and envelopes only decrease this cap. Zero state is bitwise identity.
The existing atlas is loaded and validated once; fields are built once from it and cached on
MakeHumanModel. Geometry changes on body/program/week changes, never on animation frames.

Known girths have priority. The final solver targets the selected week's girths. These are actual
starting constraints plus an explicitly modeled future projection, not silently altered facts.
If the muscle-enabled build has a specified-girth residual over 0.1 cm, compare with the same
profile without the field. If any specified constraint becomes worse by more than 0.1 cm,
drop the local field for that build and show a limitation message. This fallback does not repair
pre-existing impossible body profiles, but prevents their errors from being amplified by muscles.
Tests cover both sexes, posture, extreme profiles, volume and all specified girths.

The 3D week control reads saved weekly girths and morph values. Current/history modes stay factual;
forecast/compare show changed vertices. "Тело / Мышцы прогноза" adds planned-stimulus colors to the
already changed forecast body. The mannequin shows projected girths only, not atlas fields.

## Conservative calibration and uncertainty

Local response is a **girth residual** multiplier, not recovered muscle physiology. Requirements:
an originally saved training-aware forecast, known manual starting girth, known later manual
girths, ≥35-day horizon, ≥6 relevant trained weeks (regional S≥0.2), ≥6 weekly measurement groups
spanning ≥35 days, source confidence ≥0.8, and no critical warning. Photo measurements are excluded
from response fitting. Forecast-origin dates must precede outcomes; reconstructed/late origins
are excluded. Same fact/week cannot multiply sample size by matching multiple forecasts.

Signal = saved training-aware girth minus saved personalized composition-only girth. Require
absolute signal ≥0.2 cm and residual ratio in [0.5,1.5]. Then:
`response = clamp(1 + n/(n+24) * (medianRatio - 1), 0.8, 1.2)`.
Groups sharing a girth share its response. Otherwise response=1. The archive freezes factors,
used fact IDs and evidence cutoff. Global girth calibration excludes these regional samples.

All projected girths receive a heuristic expected range (not a confidence interval):

```
halfWidthCm = (0.3 + 0.15*sqrt(week) + 0.04*week)
              * (1 + 0.6*adherenceVariability + 0.5*photoShare)
              / sqrt(1 + min(24, observedWeeklyGirths)/8)
```

Width is zero at week zero (a model anchor, not a claim of measurement precision).
Adherence variability is the bounded coefficient of variation of completed session counts in
the preceding 12 bins; no history gives 1. It is a logging-consistency proxy, not verified adherence
to an old plan. Observed girth count pools distinct week/girth pairs; photo share retains their
provenance. A photo-derived same-date anchor also widens the range (maximum of historical
photo share and anchor photo share), without training the response. Longer horizon, sparse observations, unstable logs and photos widen the range.
There is no separate probabilistic model for vertex displacement.

## Storage and backtest

The optional `ForecastSnapshot.Muscle` extension freezes weekly adaptation/stimulus, allocations,
morph states, personalized composition-only points and local calibration. Expected points retain
regional girths and ranges. The original schema-1 checksum/CAS/append-only protections remain.
Old snapshots without programs still load. Separate `training-shape-1` version and numeric
parameters accompany the composition version. Unsupported muscle versions/corrupt states fail
validation; replay never reruns stimulus or calibration. Dictionary key order after deserialization
is not a scientific value; all saved numbers/inputs remain unchanged.

Backtest uses only previously saved numeric origins and then-available calibration; no synthetic
past forecast is reconstructed from future facts. The existing baseline/personal comparison
remains; a second comparison is personalized composition-only versus training-aware girths.
Nearest fact within ±3 days of 2/4/8 weeks, one origin/date and one fact/metric, MAE, signed bias,
counts and range coverage are retained. Girth errors are shape proxies, not muscle measurements.

## Synthetic validation and performance

Male default profile, 12 weeks, 3100 kcal/day, 3 sessions/week, 4×10/RIR2 of the named exercise.
All programs have **2.243332 kg** modeled total lean gain; **0.759406 kg** is regionally allocated:

| Program | Pectoralis allocation kg | Quads kg | Lats kg | Chest cm | Thigh cm |
|---|---:|---:|---:|---:|---:|
| Bench | 0.167069 | 0.066246 | 0.033123 | 101.853299 | 58.879715 |
| Squat | 0.022658 | 0.167069 | 0.022658 | 101.164750 | 59.118830 |
| Pulldown | 0.031171 | 0.062343 | 0.167069 | 101.889856 | 58.867452 |

With synthetic facts equal to the saved expected projection, chest MAE (composition -> aware):

| Program | 2 weeks | 4 weeks | 8 weeks |
|---|---:|---:|---:|
| Bench | 0.092388 -> 0 | 0.163782 -> 0 | 0.292164 -> 0 |
| Squat | 0.065307 -> 0 | 0.114384 -> 0 | 0.201925 -> 0 |
| Pulldown | 0.104062 -> 0 | 0.181831 -> 0 | 0.320715 -> 0 |

One synthetic origin per program, n=1 per horizon/metric, 100% heuristic range coverage.
Baseline signed chest bias is positive for bench/pulldown and negative for squat; aware bias is 0.
Thigh MAE at 8 weeks: 0.104801, 0.070042, 0.113745 -> 0 respectively. This is an intentional
replay/plumbing check with self-generated facts, **not an independent accuracy benchmark or
evidence that the model predicts real hypertrophy**. Separate semantic tests establish region
differentiation, caps, zero/negative energy gates, recovery/decay, experience and symmetry.

Windows .NET 10 Release, same model/profile, six warm samples after two warmups:
median rebuild **1.32 ms off / 1.65 ms on**. This includes fitting, excludes first atlas/field load.
Real Edge WebAssembly smoke: repeated muscle-enabled rebuilds typically **27–41 ms**;
first builds after asset/profile setup **126–157 ms**, including existing fit/rig/upload work.
These are local measurements, not a hardware-independent guarantee. Extreme fallback can do
two builds. No runtime per-frame CPU morph work or atlas regeneration was added.

Validation commands: `dotnet build WorkoutCalculator.sln -c Release`,
`dotnet test WorkoutCalculator.sln -c Release --no-build`, `node --test tests/js/*.test.mjs`.
Release: 0 warnings/errors; .NET **364/364**; JS **36/36**. CI runs all five browser smokes:
skeletal, strength, history, personalized forecast and muscle forecast. New smoke covers real
squat+bench editor, equal-energy bench-vs-squat geometry, mass cap, color-only toggle, compare,
save/reload, historical fact updates and 320/390/1400 px. Existing smoke covers photos, cardio,
animations, tapes, storage errors and personalized forecast regressions.

## Remaining limitations

No promised hypertrophy rate or per-muscle mass measurement; fixed heuristic priors/coefficients;
limited catalog; recurring weekly plan rather than a workout planner; no unilateral semantics;
no within-week recovery timing; no exercise-specific weight reference estimation; no independent
calf girth equation; procedural atlas targets; coarse shared girth response; sparse local-data
calibration; no clinical or real-world predictive validation. Actual training and manual longitudinal
girths improve evidence, but cannot fully separate fat/water/muscle or adherence from response.
