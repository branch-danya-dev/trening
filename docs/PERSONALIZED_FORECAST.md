# Personalized forecast v1

Scope: weight, recorded body-fat percentage and existing girths. No muscle-growth prediction, ML, account/backend/sync, or attribution of body changes to individual workouts. These are model projections, not medical recommendations or promises.

## Baseline and architecture

`ForecastEngine.Run(profile, input, calibration = null)` retains the original weekly Hall/Forbes calculation in `RunBaseline`; null uses the same code and results as main `379ba38`. A pinned reference test additionally checks weight and glycogen/water at week 2. No new data enters the baseline calculation.

The optional `ForecastPersonalization` layer adjusts **cumulative tissue changes**, leaving the baseline's glycogen/water series untouched. This avoids rewriting the energy model or fitting short-lived water fluctuations. Baseline expenditure/BMR/adaptation values and warnings remain baseline diagnostics, not independently measured personal expenditure. `ForecastEngine.ModelVersion` is `hall-forbes-1+residual-1`.

Responsibilities:

- `ForecastSnapshot`: frozen input/profile JSON, model/version and numeric constants, optional same-date starting `BodySnapshot`, calibration revision and coefficients, weekly baseline/expected points including girths and ranges, hypothesis identity/name, timestamps and horizon.
- `ForecastEvaluationService`: facts against stored points, interpolation, source provenance and eligibility.
- `ForecastCalibrationService`: conservative deterministic weekly residual fitting and exclusion diagnostics.
- `ForecastUncertainty`: heuristic expected weight range.
- `ForecastBacktestService`: historical baseline/personalized scoring at saved forecast origins.
- `ForecastStore` / `PersonalizedForecastState`: strict browser persistence, append-only histories, CAS and UI state.

## What is saved and what is a preview

Moving a slider calculates a preview; it does not silently issue hundreds of historical forecasts. **Save forecast and start plan** appends a version. Every such issue is saved, even if its inputs match another version. A saved version cannot be edited or deleted through this API/UI. Changes to a plan or profile start a new preview.

Frozen JSON thaws into fresh mutable domain objects. Arrays and dictionaries inside archives are immutable. `Replay()` builds the result and its 3D endpoint from **saved numeric points**; it never calls the current engine. Thus even an unknown future/old model-version string can be displayed without recomputing history. The saved start, input and coefficients allow investigation; exact historical reproduction is numeric replay, not a promise that future algorithm code can recalculate old versions.

The current date uses the browser's local calendar. A same-date selected history fact supplies only its known weight/BF/girths; otherwise a same-date weight fact is preferred. Remaining parameters come from the current profile and are model inputs. The user sees the starting weight before saving. The explicitly submitted initial weight is the weight initial condition; BF and girth **learning** additionally require actual same-date starting measurements. Historical facts never overwrite the live profile.

## Evaluation

Facts are included only when `0 < factDate - startDate <= 7 * horizonWeeks`. Missing values create **no error row**, never zero. Weight-only records do not create BF/girth errors. Imported photos use `BodySnapshot` measurements; the old photo metadata's inherited profile weight/BF is not read as a measurement.

For fractional week `t`, with `i=floor(t)` and `u=t-i`, interpolate each stored numeric quantity as `a[i] + u*(a[i+1]-a[i])`. Weight, tissue masses, water and girths interpolate separately; predicted BF is `100 * interpolatedFatMass / interpolatedWeight`. There is no extrapolation beyond the saved horizon. Signed error is **actual - predicted**; absolute error is its absolute value. Each row retains fact/forecast IDs, metric, date, horizon days, baseline prediction, initial known value, provenance, source weight and exclusion reason.

Source weights are fitting weights, **not probability of correctness**:

| Source | Weight before optional recorded confidence |
|---|---:|
| Manual | 1 |
| Imported weight journal | 0.8 |
| Photo-derived | `0.5 / (1 + RMSEcm/5)`; missing RMSE uses 0 in this weight only |

Use the lower of start/end weights when a starting fact exists. Photo session/reference, method and recorded RMSE remain in provenance. Unknown RMSE is labelled unknown, not a measured zero error. Existing imported photo parse warnings are conservatively all excluded; this does not require modifying existing BodySnapshot/history schema.

## Calibration formulas and smoothing

All observations are evaluated only through the revision's cutoff date, from forecasts created by that time. Duplicate `(fact ID, metric)` evidence across multiple forecasts is counted once: prefer an eligible forecast, then the latest start date, then stable ID order. Saving many forecasts cannot manufacture additional sample size.

Define for a weight observation:

```
W0 = submitted starting weight
Wb = baseline predicted weight at the fact date
G  = baseline glycogen/water shift at that date
x  = Wb - W0 - G                 # baseline tissue change
y  = Wactual - W0 - G            # observed change after modelled water
r  = y / x
```

Require horizon >=14 days and `abs(x) >=0.5 kg`. Exclude weight outliers when `abs(Wactual-Wb) > max(5 kg, 0.08*W0)` or raw `r` is outside [-1,3]. Clip remaining ratios to [0.5,1.5]. Group by the absolute seven-day bucket `DateOnly.DayNumber / 7`; within each bucket use median ratio and median absolute baseline error, with mean source weight. Daily/same-day duplicates cannot count as additional weeks. Symmetric short-term weight noise is reduced by the median, not fitted as a daily change.

Require at least **4 weekly groups spanning at least 21 actual calendar days**, and sum of weekly quality weights >=2. Below these thresholds, weight response remains exactly 1. With `n = sum(weekly weights)` and `m = weighted median(weekly ratios)`:

```
shrinkage = n / (n + 8)
WeightResponseFactor = clamp(1 + shrinkage*(m-1), 0.75, 1.25)
EnergyBalanceFactor = 1
```

Energy/expenditure and weight response are not separately identifiable from weight residuals. **v1 fixes EnergyBalanceFactor at 1** instead of double-fitting the same signal. The response factor can include adherence, measurement error and model mismatch; it is not a diagnosis of metabolism. Four to seven accepted weeks are labelled initial calibration; eight or more are labelled personalized. BF/girth evidence has separate gates shown in diagnostics.

For a baseline point with cumulative changes `dFat`, `dLean`:

```
s = WeightResponseFactor * EnergyBalanceFactor
dTissue = (dFat + dLean) * s
fat = startFat + dFat*s + dTissue*FatLeanPartitionCorrection
lean = startWeight + dTissue - fat
weight = fat + lean + baselineWater
```

Week 0 and identity correction retain baseline values exactly. Nonidentity corrections keep fat within the existing sex-specific essential-fat bound and leave positive lean mass. Invalid/extreme trajectories cannot be saved. Girths first use the existing `ApplyToGirths` tissue-to-volume model, then the per-girth change multiplier, without converting water into girth change.

### BF partition

Only known starting BF and later **both measured weight and BF** can fit partition. Require at least 1 kg absolute observed and baseline tissue change. Exclude BF error >8 percentage points. Estimate:

```
observedFatDelta = BFactual/100 * Wactual - BFstart/100 * W0
partitionResidual = observedFatDelta / observedTissueDelta
                    - baselineFatDelta / baselineTissueDelta
```

Clip residuals to [-0.16,0.16], use weekly medians and require 6 groups over >=35 actual days with effective n>=3. Shrink weighted median by `n/(n+12)` and bound final correction to [-0.08,0.08]. Missing starting BF or paired weight leaves correction at zero. This is a conservative residual partition correction, not measured muscle gain.

### Individual girths

Require known starting girth and a known later value. Predict the change using the revised weight/partition coefficients first, so those adjustments are not applied twice. For each girth, divide observed change by that predicted change. Require absolute predicted change >=0.5 cm; exclude error >10 cm and raw ratios outside [-1,3]. Use clipped [0.5,1.5] weekly ratios, at least 6 weeks spanning >=35 days, effective n>=3, shrinkage `n/(n+8)` and factor bounds [0.8,1.2]. A girth without enough evidence has no multiplier (identity).

## Expected range

This release stores/displays an **expected weight range**, not a statistical confidence interval or P10/P90. For week `h>0`:

```
e = min(24, effectiveWeightObservations)
errorScale = max(0.5 kg, median-per-week historical baseline MAE)
qualityScale = 1 + 0.5*(1 - meanWeeklySourceWeight)
halfWidth = (0.35 + 0.35*sqrt(h) + 0.08*h)
            * errorScale/0.5 * qualityScale / sqrt(1 + e/8)
lower = max(0.01, expected - halfWidth)
upper = expected + halfWidth
```

Week 0 has zero width around the fixed starting input. With other terms fixed, width increases with horizon and decreases with quality evidence; greater observed error can appropriately widen it again. `historical baseline MAE` here is the mean of weekly median absolute baseline errors. Below activation thresholds `e=0`. Saved ranges do not change after a new revision. BF/girth ranges and alternative 3D range bodies are not supplied in v1; 3D uses personalized expected values.

## Historical backtest without look-ahead

Backtesting is **prequential scoring of saved forecast origins**, not retrospective fitting of arbitrary dates with today's profile. Each origin T already freezes baseline and then-personalized predictions. Require `CreatedAt.localDate <= T`, matching stored calibration, `revision.CreatedAt <= forecast.CreatedAt` and `revision.ThroughDate <= T`. Reconstructed/backdated origins are excluded. Keep the earliest issued forecast per origin date to avoid multiplying scores by repeatedly saving a plan.

At 2/4/8 weeks, select the nearest known fact per metric within +/-3 days, break ties by quality/date/stable ID, and compare with interpolation on its **actual date**. No future-of-report-cutoff facts are scored. Invalid/poor-source/photo-warning outcomes are excluded from scoring; an unknown starting girth can prevent learning while still allowing scoring against a known later girth. No matching outcome means n=0 and undefined metrics, not zero error.

For each horizon and each weight/BF/girth metric, report baseline and personalized separately:

- `MAE = mean(abs(actual-predicted))`;
- `bias = mean(actual-predicted)`;
- `n = number of matched outcomes`;
- weight range coverage = fraction of actual weights inside that method's stored range.

Adding a later calibration revision never changes a previously issued personalized forecast. Correcting an outcome can change a report of actual error; it cannot change the historical prediction or its calibration. New backfilled facts create a revision **now** and are not retroactively available to older forecast origins. The app needs prospective saved origins before personal historical accuracy is available; the UI shows insufficient data until then.

### Synthetic reference results

`SyntheticSequentialOriginsImproveOrMatchBaselineWithoutLookAhead` issues four sequential origins 8 weeks apart. Initial profile: male, 35, 180 cm, 100 kg, 30% BF; intake 2100 kcal, activity 1.3, strength 3/week. Outcomes have tissue response 0.8/1.0/1.2 of baseline with baseline water unchanged. Each revision uses only already-observed outcomes. These are deterministic implementation checks, not external validation on people.

| True response | Horizon | n | Baseline MAE kg | Personal MAE kg | Baseline bias kg | Personal bias kg |
|---|---:|---:|---:|---:|---:|---:|
| 0.8 | 2 weeks | 4 | 0.124171 | 0.068739 | 0.124171 | 0.068739 |
| 0.8 | 4 weeks | 4 | 0.243395 | 0.134756 | 0.243395 | 0.134756 |
| 0.8 | 8 weeks | 4 | 0.468348 | 0.259323 | 0.468348 | 0.259323 |
| 1.0 | 2/4/8 weeks | 4 each | 0 | 0 | 0 | 0 |
| 1.2 | 2 weeks | 4 | 0.119549 | 0.067171 | -0.119549 | -0.067171 |
| 1.2 | 4 weeks | 4 | 0.234326 | 0.131679 | -0.234326 | -0.131679 |
| 1.2 | 8 weeks | 4 | 0.450882 | 0.253398 | -0.450882 | -0.253398 |

Weight range coverage is 100% for both methods in these deliberately simple scenarios. That does **not** establish calibrated coverage in real use. Unit tests also cover measured girth/partition fitting, noisy daily weight, one outlier, minimum evidence and fixed baseline reference points.

## Exclusion and history

Evaluation can show a row while calibration excludes it for invalid snapshot, unknown starting field, poor source quality (<0.35), photo warning, horizon <14 days, insufficient signal, outlier, reconstructed origin or issue after fact. Missing/nonfinite actual values create no fabricated numeric errors; invalid records also have revision diagnostics. All existing photo parse warnings are conservatively treated as exclusions rather than guessing severity from prose.

Every distinct fact-state fingerprint creates a new immutable `CalibrationRevision`, including edits/deletions/imports. It records its predecessor, creation/cutoff dates, evidence fingerprint, scalar coefficients, per-girth coefficients, evaluated observations, source weights and exclusion reasons. Opening/reloading unchanged data performs no write. A corrupt Body History store cannot be misread as deletion of all facts. The fingerprint is for change detection, not anonymity or security.

## Storage and migration

`workoutcalc.forecasts.v1` contains a schema-1 envelope with a JSON payload and SHA-256 checksum. The payload has immutable forecast and calibration arrays. Reads validate checksum, schema, IDs, linked revisions, required constructor fields, trajectories, bounds and finite numbers. Save appends to the previously read payload using the same strict synchronous browser CAS boundary as body/strength history. Stale-tab, quota/read failure, malformed JSON, semantic corruption or future schema return errors and preserve original bytes. SHA-256 detects accidental corruption; it is not authentication against someone editing localStorage.

There was no previous numeric forecast archive. Old started hypotheses contain only plan inputs and a starting profile. **Transfer old plans to history** is an explicit, idempotent migration: strict-read the old key, keep a byte-for-byte backup at `workoutcalc.forecasts.v1.backup.legacy-hypotheses`, preserve old keys, then CAS-append reconstructed snapshots. Their label and flag identify that points were calculated now; they cannot train or enter historical backtesting. A failed CAS can leave the harmless backup in place but cannot partially replace the main archive. Future schema migration requires an explicit new implementation; unknown versions remain write-blocked.

## UI and verification

The hypothesis tab contains a compact accuracy card, saved version selector, explicit issue/new-preview actions, a weight error summary, expected range and expandable evaluation/backtest/diagnostics. The existing chart shows factual BodySnapshot weights, optional baseline, personalized expected line and a range band. Historical selection replays one archived series; editing plan/profile returns to preview. Current/forecast/compare/Body History, photo warp, cardio, strength, atlas and animations retain their existing renderer paths.

Required local commands:

```
dotnet build WorkoutCalculator.sln -c Release
dotnet test WorkoutCalculator.sln -c Release --no-build
node --test tests/js/*.test.mjs
```

Validation at this change: **312 .NET tests; 36 JS tests; build 0 warnings/errors**. CI runs the existing skeletal, strength and history browser smokes plus `tests/browser/forecast-smoke.cjs`. The new smoke uses the real WASM app, creates a version, adds dated facts, learns a revision, checks changed personal/available baseline/range/3D, verifies historical immutability and reload, checks widths 320/390/1400, and exercises stale-tab/corruption protection. Local browser runs use Edge/Chromium; CI uses its pinned Chromium installation.

## Limits and remaining work

- Residual response is not a causal explanation and may not generalize to a changed plan, adherence or measurement protocol. Sparse/biased measurements can mislead any deterministic correction; shrinkage and bounds limit, rather than eliminate, that risk.
- Weekly summaries remain temporally correlated. Observation counts are not statistically independent sample-size claims. There is no formal confidence level.
- Energy factor stays fixed; weight range only; no muscle-specific growth; no training-effect attribution.
- Accuracy requires genuinely issued historical versions. Backfilled/reconstructed forecasts do not manufacture historical skill.
- LocalStorage has finite quota; failures block appends without deleting history. No cloud backup/sync is introduced.
- PR #10 remains open for its distinct unified backup, onboarding and calendar work; see [PR10_AUDIT.md](PR10_AUDIT.md). Its older data architecture is not merged.
