# Forecast v3 / Stage A: composition refinement

Implementation: `hall-forbes-2+residual-1`. Scope is numerical composition only. Facts, visual estimates, forecasts and renders remain separate. No BodyParts3D, DeltaShape, image rendering, MakeHuman fitting or muscle geometry was replaced.

## Base and order of work

On 2026-10-09 PR #17 and #18 were checked, then merged with ordinary two-parent merge commits:

- #17 → `9872660c63455f37670a0367c1bc5829a39c62d3`; [main CI passed](https://github.com/branch-danya-dev/trening/actions/runs/37870214874).
- #18 → `471449780756f091234849d3055bd1e149f9312e`; [main CI passed](https://github.com/branch-danya-dev/trening/actions/runs/37870594541), including all eight existing browser suites and published PWA offline.
- Baseline main is exactly `471449780756f091234849d3055bd1e149f9312e`. No rebase, force-push or product workaround was needed. Local baseline: 372 .NET tests, 43 JS tests, Release 0 warnings/errors.
- Harness and pre-change numerical snapshot were committed locally **before** production equations changed, then published as `009a3eb2b164ea5f1bdbc43395a2ec32db7d1bee` (identical Git tree). [Legacy report](FORECAST_V3_COMPOSITION_LEGACY.md) is reproduced by the retained legacy runner. Its JSON scores are unchanged.

The separate Pages deployment still fails with the existing disabled-Pages 404; its build succeeds. This is a repository hosting setting, not a failing application suite. No hosting setting was changed. The new Stage A PR is not auto-merged.

## Published sources and boundaries

1. [Hall et al. 2011, DOI 10.1016/S0140-6736(11)60812-X](https://pubmed.ncbi.nlm.nih.gov/21872751/) and [official equation appendix](https://www.niddk.nih.gov/-/media/Files/BWP/Hall_Lancet_Web_Appendix.pdf): diet-linked AT, fixed TEF reference, glycogen and ECF differential equations, tissue energy partition. Source/version/hash/license decision and numerical oracle assumptions are in [REFERENCE.md](../tools/WorkoutCalculator.CompositionBenchmark/REFERENCE.md).
2. [Westerterp 2004, DOI 10.1186/1743-7075-1-5](https://pubmed.ncbi.nlm.nih.gov/15507147/): reported TEF ranges protein 20–30%, carbohydrate 5–10%, fat 0–3%. Our fixed points 25%, 7.5%, 2.5% are choices within those ranges, not individually measured metabolism.
3. [Westerterp-Plantenga et al. 1999](https://pubmed.ncbi.nlm.nih.gov/10403587/): controlled isocaloric macro-composition study supports the direction/modest scale of TEF differences; not a calibration dataset for our coefficients.

No full substrate network, gluconeogenesis, ketogenesis or protein-turnover model is imported. Published equations do not make our complete hybrid model the official NIDDK planner. No remote code, patient data, weights or PDF is shipped. The oracle is independent research code, not a runtime dependency.

## 1. Intake and activity boundary

Let `B0` be initial Mifflin BMR, `A` household activity multiplier, `X0` planned active training kcal/day, `I` planned intake, `I0` habitual maintenance intake.

```
I0 = BaselineIntakeKcalPerDay ?? (B0 * A)
DietEnergyChange = I - I0
ActivityEnergyChange = (B0 * A + X0) - I0
pre-adaptation energy change = DietEnergyChange - ActivityEnergyChange
```

The optional baseline input means habitual intake **at stable weight**. If absent, structured training in the plan is treated as added activity. If it is already habitual, set the baseline to actual maintenance including that activity. Changing the household multiplier without a measured baseline still has an identification limit; no hidden estimate of previous activity is invented. These quantities are model assumptions, never new BodySnapshots.

Mifflin BMR, active-cardio expenditure, strength MET approximation and their weight scaling are retained. The bodyweight-dependent expenditure reduction is distinct from intake-linked AT. Production still uses weekly tissue steps, unlike the daily reference integrator.

## 2. AT and TEF

Hall appendix equation 7:

```
targetAT = 0.14 * (I - I0); tau = 14 days
decay = exp(-7/tau)
meanAT = targetAT + (ATstart-targetAT) * tau/7 * (1-decay)
ATend = targetAT + (ATstart-targetAT) * decay
```

AT is driven by intake. A +500 kcal activity intervention with unchanged intake has zero AT target; an equal -500 kcal diet intervention has -70 kcal/day asymptotic AT. Training expenditure continues to affect total energy balance.

Fixed fallback: `deltaTEF = 0.10*(I-I0)` (Hall equation 6). Macro path, with normalized P/C/F grams:

```
macroEnergy = 4P + 4C + 9F
TEFabsolute = .25*4P + .075*4C + .025*9F + .10*unspecifiedEnergy
deltaTEF = TEFabsolute - .10*I0
expenditure = weight-dependent base expenditure + meanAT + deltaTEF
balance = I - expenditure
```

The initial TEF is assumed to be 10% of habitual intake because habitual protein/fat are not measured. This is our boundary assumption; even isocaloric changes can therefore change TEF. No user data fitted these coefficients. In the pinned 2000 kcal test, replacing 400 kcal fat with protein changes TEF by 90 kcal/day, not hundreds of percent of expenditure.

Validation:

- All optional gram inputs must be finite and nonnegative. Null is unknown; zero is a known zero.
- All three specified: energy must be within ±5% of total intake; normalize proportionally to intake and warn when changed. Keep original typed values in input JSON and snapshot.
- Partial macros: missing energy uses 10% TEF, never zero calories. Known energy above 105% is rejected. If known energy is only slightly above total (≤5%), normalize it; otherwise leave it unchanged.
- Gross inconsistencies, NaN/Infinity and negative values raise validation errors. The advanced editor commits all fields together after validation. Failed drafts do not mutate the stored plan. Imported invalid alternatives are explicitly excluded from comparison with an error, not silently corrected.

## 3. Carb-driven glycogen, with separate bound water

When planned carbs are known, `C0 = BaselineCarbsGramsPerDay ?? .5*I0/4`. The 50% baseline share is our heuristic and marked assumed in metadata. Explicit baseline carbs must be positive and energetically possible for I0. An absolute planned carb value alone does not identify the actual diet change.

Hall appendix equation 1 uses `G0=.5 kg`, `rhoG=17600/4.184 kcal/kg`, `k=4*C0/G0²`, `rhoG*dG/dt = 4*C-k*G²`. We use its exact constant-input step solution:

```
a = k/rhoG; target = G0*sqrt(C/C0)
q = (Gstart-target)/(Gstart+target)*exp(-2*a*target*days)
Gend = target*(1+q)/(1-q)
if target=0: Gend = Gstart/(1+a*Gstart*days)
```

Production caps the carb ratio at 3 with a warning: a conservative domain restriction, not a fitted coefficient. The independent reference is uncapped. Baseline glycogen is not measured. `GlycogenKg` is change from initial glycogen; `BoundWaterKg=2.7*GlycogenKg`; compatibility field `GlycogenWaterKg` is their sum.

```
weekly tissue energy = 7*balance - rhoG*(Gend-Gstart)
scale weight = fat + baseline-FFM-with-tissue-change + glycogen change + bound water change
```

Only glycogen stores energy. Bound water does not consume/produce tissue calories. The original baseline FFM already includes habitual glycogen/water; only changes are added, avoiding double counting. Girth/muscle allocation sees tissue changes only. Model lean is not a direct muscle measurement.

If carbs are null, the exact legacy balance-based pool/rate/energy approximation remains, with its existing 0.015 lean pool, 1000 kcal full effect and 0.6 weekly rate. It does not acquire a fabricated gram breakdown. With null macros and an explicit baseline equal to legacy maintenance, fixed TEF and water reproduce the old numerical path (apart from new transparent metadata). Without that explicit baseline, AT separation intentionally changes the old training assumption.

The refeed test applies two successive constant-input glycogen phases to the same state. Production UI still issues a constant-plan forecast, not an editable daily diet simulator.

## 4. Sodium / ECF: NO-GO for production

`SodiumMgPerDay?` accepts finite 0–10000 mg as optional research metadata, is preserved by snapshots/backups, and has **no numerical effect**. A supplied value produces an ECF-off warning. No sodium field is exposed in the production editor.

The tools-only overlay analytically solves Hall equation 2 using 3220 mg/L extracellular concentration, 3000 mg/L/day sodium feedback and 4000 mg/day carb-dependent natriuresis. Tests cover no change, increase/decrease, carb interaction and bounded equilibration. It adds scale water only, never feeds tissue expenditure. The full reference separately includes ECF's small weight/expenditure coupling, and is labeled as such.

Reason for NO-GO: habitual sodium, hydration, medications and fluid regulation are unobserved; synthetic agreement cannot establish benefit for this product. Daily hydration variability remains in the existing heuristic uncertainty range, not in fitted daily-weight coefficients.

## 5. Tissue model and domain

Existing Forbes deficit partition, strength preservation factor, strength-dependent gain ceilings, regional girth conversion and uncertainty are retained. These remain a hybrid of published relationships and product heuristics; the strength factor/gain ceilings are not the Hall reference. No new coefficient was tuned against users or the synthetic oracle.

For the new version only, a proposed trajectory with lean below `min(initialLean, max(15 kg, .5*initialLean))`, nonpositive fat or nonfinite changes is rejected, not extrapolated into an invented plateau. The bound is a documented numerical/domain guard, not a physiological limit claim. The editor shows the error and disables saving that forecast; valid extreme profiles at 52 weeks are tested. Legacy evaluation remains unchanged.

## 6. Versions, archives, calibration and historical scoring

- `hall-forbes-1+residual-1`: retained via `ForecastEngine.RunVersion`; same pinned legacy numeric results, useful for offline A/B and rollback.
- `hall-forbes-2+residual-1`: new default. AT `diet-at-2`; TEF `fixed-10%-1`, `partial-macro-tef-1` or `macro-tef-1`; glycogen `balance-glycogen-1` or `carb-glycogen-2`; ECF always off.
- `Capture(modelVersion)` retains all old constants and captures new TEF, normalization, glycogen/water, assumed-carb and domain coefficients. Composition metadata freezes modes, assumed/explicit baselines, reference version and benchmark limitation.
- Old input fields remain optional. Old snapshots have null composition metadata. `Replay()` reads saved points and never calls any engine; numeric outputs are not migrated. The pre-change JSON fixture is never regenerated. Both typed serialization and the browser load/replay it.
- `CalibrationRevision.CompositionModelVersion` defaults to legacy when absent in old JSON. New `Build` filters origins by explicit engine version before choosing one fact/metric observation. Old/new errors never train one revision. The global PreviousRevisionId remains archive append order, not cross-model reuse of coefficients.
- CurrentRevision selects only the new engine. Old revisions remain stored for replay. Supplying a mismatched revision or facts beyond an origin fails. Muscle-response evidence for newly issued snapshots is likewise restricted to the same composition version.
- Historical backtest uses saved baseline/expected points and then-available revisions only. ByModel presents legacy baseline/personalized and v3 baseline/personalized rows separately, at 1/2/4/8/12/24 weeks. No v3 score is backfilled when only legacy origins exist. Same-date duplicates are deduplicated within each model. Day-7 outcomes can be scored but still cannot train calibration before day 14.

## 7. Results and acceptance interpretation

[Generated report](FORECAST_V3_COMPOSITION_BENCHMARK.md), [all strata JSON](FORECAST_V3_COMPOSITION_BENCHMARK.json), [legacy baseline](FORECAST_V3_COMPOSITION_LEGACY.md). 4096 profiles, six horizons, independent local Hall integrator. Targeted carb-water agreement and all-horizon aggregate weight MAE improve. At week 1 weight MAE is 0.254 → 0.055 kg; week 24 0.788 → 0.703 kg. ECF-inclusive week-24 MAE is 1.021 → 0.929 kg.

Negative results are retained: week-24 fat MAE 0.397 → 0.496 kg, lean MAE 0.823 → 0.853 kg; weight MAE worsens for large deficit (0.480 → 0.541), maintenance (0.403 → 0.504), no-strength (0.519 → 0.525). Hall's fixed TEF and absence of strength-specific partition limit interpretation of those differences. They are not suppressed or used to retune coefficients. Do not claim improved physiological fat/lean accuracy from this study.

Stage A composition mechanisms: **GO for review**, based on targeted measurable reference agreement, independent energy/version/replay checks and preserved product regressions. Sodium/ECF: **NO-GO**, research only. Human physiological accuracy: **unproven**; continue the prospective protocol in VALIDATION. No personalized parameters are trained against the reference. Zero-evidence personalized v3 equals baseline; nonzero residual response is tested with sequential, then-available synthetic observations.

## 8. Verification and performance

Commands and mobile/browser coverage are in [VALIDATION](VALIDATION.md#composition-v3--stage-a). Reference tests pin equilibrium, analytic AT, glycogen/ECF equilibria, RK4 refinement, deterministic seed/hash and report regeneration. Broad maximum-divergence bounds catch catastrophes; exact equality is required only for deterministic artifacts and legacy replay, not every physiological result.

Local verification: 409/409 .NET tests, 43/43 JavaScript tests, Release build with 0 warnings/errors; eight existing browser suites plus the new composition suite, including 320/390 px and published PWA offline. Both generated reports pass `--check`; JSON output uses explicit LF newlines. The first Linux CI exposed native-math last-bit differences in the scenario hash. Fingerprint serialization now rounds derived input fields to the report's six-decimal precision, without changing scenario generation or any benchmark score; both the original and canonical hash contracts are documented in REFERENCE.md. The PR CI independently repeats the checks on Linux.

[Performance artifact](FORECAST_V3_COMPOSITION_PERFORMANCE.json): Release .NET 10.0.12, Windows, 7 alternating-order rounds ×4096 forecasts, 24 weeks. Median legacy 0.1416 ms/call; v3 0.1411 ms/call; personalized v3 0.2795 ms/call. This small difference is timing noise, not evidence of a speedup. The test includes v3 optional-input allocation but excludes 3D, storage and browser. No new network/asset dependency is added to the PWA; full browser timing depends primarily on the existing shape/mesh pipeline.

## Phase 4 nutrition evidence adapter

`ClosedDayNutritionAggregator` reads validated, fully recorded closed nutrition for one profile/date window with an explicit as-of cutoff. It returns kcal/P/F/C means and coverage/variance/warnings. MissingData, Open, Partial and legacy unknown nutrition do not become zero-intake days; physical RestDay may contain food. Nullable macros preserve Stage A fallback. Label values remain authoritative; if incompatible with the existing 5% engine gate, mapping emits a warning and sends only calories. All-zero fasting evidence remains factual but cannot map into the positive-intake engine domain. `NutritionTarget` is an adapter view of an explicitly saved Forecast plan, independent of actual food. No physiology coefficients, snapshot replay, sodium/ECF production status or Hypothesis lifecycle change. Exact rules: [NUTRITION_V1](NUTRITION_V1.md#forecast-v3-boundary).

## Phase 5 observed-routine adapter boundary

ObservedRoutineForecastAdapter version observed-routine-pal-1 uses complete closed nutrition and frozen activity. ActivityFactor = clamp(1.2 + known daily non-strength active kcal / starting BMR, 1.2, 3); explicit Cardio is null/0 so the same event cannot count twice. Strength frequency follows one separate generic-strength path. Unknown-energy days are excluded from the energy mean; missing mean remains null and the baseline-only assumption is disclosed. No Stage A coefficients or old weekly numerical points change. Exact 30d endpoint interpolates week 4→5, separately from the snapshot. [Full policy, proof tests and limitations](HYPOTHESIS_LIFECYCLE.md).
