# Nutrition v1 — Phase 4

**Meal plan is expectation; actual MealEvent is fact; ClosedActivityDay nutrition is frozen evidence.**

Baseline main: `0f943cc4fdd637d30d71f7d7519bb0aaf0d8e05c`. #23 merged with merge commit `57cd2fb9a94eef168ad06430ff641383e5eb64af`; [post-merge CI](https://github.com/branch-danya-dev/trening/actions/runs/37887465343) passed. #19 merged with merge commit `0f943cc4fdd637d30d71f7d7519bb0aaf0d8e05c`; [post-merge CI](https://github.com/branch-danya-dev/trening/actions/runs/37888370664). No history rewriting or conflict resolution was needed. Combined tree `c7a8338cd6f1ee2c4d068af75774b74cec26eab2` matched final main exactly: 489 .NET, 54 JS, composition/activity smoke and Hall report reproduced before the second merge.

## Model and ownership

| Data | Source of truth | Mutation policy |
|---|---|---|
| Default/per-day meal slots | `DayPlan.MealSlots` in ActivityDay envelope | Template affects future days; per-day plan editable while Open |
| Actual food label | `NutritionReference`, copied into each entry | Immutable value; edit replaces the open entry |
| Consumed grams | `FoodConsumptionEntry.ActualGrams` | Open-day meal editor only |
| Meal grouping, time, slot reference | `ActivityDay.Meals : MealEvent[]` | Save/delete/move through protected ActivityDayStore |
| Running totals | Derived from actual entries | Never from meal plan or a future dictionary |
| Explicit target | Read-only `NutritionTarget` adapter from selected saved Forecast plan | No new target editor/store; invalid/unset targets omitted |
| Closed totals, coverage, target snapshot | `ClosedActivityDay.Nutrition` | Terminal, immutable; no reopen/amend |
| Physical activity | Existing physical events/journal references | Existing ownership and source guards retained |

`NutritionReference`: name, `Per100Gram`/`Serving`, reference grams, label kcal, optional protein/fat/carbs, source `Manual`, schema 1. `FoodConsumptionEntry`: UUID, reference snapshot, actual grams, optional private note, derived totals. `MealEvent`: UUID, DayId, Breakfast/Lunch/Dinner/Snack/Custom, optional custom label, planned-slot ID and actual time, nonempty entries, CreatedAt/UpdatedAt, schema 1. IDs are unique across days/events/meals/entries; a duplicate is rejected rather than double-counted.

Meal times are organisational only. A late breakfast has the same totals as an early one. Plan slots never create actual meals. Actual meals may be unplanned. Editing/removing a slot does not rewrite actual food. Initial defaults: breakfast 08:00, lunch 13:00, dinner 19:00, optional snack without time. Labels/time/slot count are editable in Profile template and the open day.

## Arithmetic and validation

Production uses C# `decimal`. Each entry computes `labelValue * actualGrams / referenceGrams`; Per100Gram requires exactly 100 reference grams. The multiplication occurs before division. Each derived entry value is rounded once to six decimal places, midpoint-to-even, then summed without display rounding. UI displays up to two decimals. Label values and actual grams are retained exactly to six decimal places. A missing macro remains null, including a daily macro with any unknown contributing entry; a known zero is allowed.

| Example | Reference | Consumed | Result |
|---|---|---|---|
| Макароны по-флотски | 350 g; 700 kcal; P35/F25/C80 | 175 g | **350 kcal; P17.5/F12.5/C40** |
| Per100Gram | 100 g; 60 kcal; P3/F3/C5 | 250 g | 150 kcal; P7.5/F7.5/C12.5 |

Positive reference/actual grams: 0.01–10000, at most six decimals. Negative, zero grams, NaN, Infinity, exponent/thousands syntax, excess precision and out-of-range numbers are rejected. Russian comma and dot both work. Zero calories/macros remain valid for label values such as water; negative values are invalid. Calories ≤10 kcal/g; each macro ≤reference mass; macro sum ≤105% mass to tolerate labels; names ≤200 chars, notes ≤1000; at most 100 meals/day and 100 entries/meal. These are data sanity limits, not nutrition recommendations.

Label calories are authoritative. If all macros are known and the 4P+9F+4C difference exceeds max(1 kcal, 5%), show a warning, preserve the entered values and include mismatch count. No silent reconciliation. Calories-only logging works.

## Coverage and closure

| Coverage | Meaning | Whole-day evidence |
|---|---|---|
| NotRecorded | No reliable nutrition entries; totals null | Excluded |
| Partial | At least one entry, no full-day confirmation | Excluded; recorded kcal are a lower-bound observation |
| Complete | User explicitly checked “Всё съеденное за день внесено?” | Eligible subject to closed state, date and as-of guards |

Blank + Complete is rejected unless the separate “еды за день не было” confirmation is also set. Only that dedicated NoFood condition produces explicit zero kcal/macros. NoFood with entries or without Complete is rejected. Complete means all intake recorded; it does **not** mean all macros are known. Closed calories-only data therefore retain null macros.

Completed accepts actual physical events and/or meals. RestDay still requires no recorded physical events and **may contain normal food**. MissingData is terminal with Closure=null, even if partial draft meals are retained; it is excluded from full-day nutrition evidence. Old Phase 3 closures have Nutrition=null, which remains unknown, never 0 kcal.

Review displays actual meals, physical summary, totals, coverage warning and optional target comparison. Cancel returns to editing. Confirmation freezes totals, meal and entry counts, coverage, NoFood flag, sorted Meal IDs, target snapshot, mismatch count, nutrition schema 1 and model `nutrition-decimal-1`. Source entries remain protected in the same envelope. Stored closure validation recomputes and compares the summary with actual immutable entries.

## Storage, migration and backup

Keep key `workoutcalc.activityDays.v1`; **envelope/payload schema 2** now owns nutrition alongside physical data. No separate store or cross-store nutrition transaction. SHA-256 verifies payload integrity; strict validators reject corruption, duplicate identities, inconsistent frozen totals and future schemas while retaining original bytes and blocking writes.

Explicit schema-1 read migration supplies empty new collections, adds default meal slots to the template and Open plans only, and leaves old closures without nutrition. Reading does not write; the next normal CAS write persists schema 2. Repeated indexing is idempotent. A v2 payload missing required collections is rejected rather than interpreted as legacy. Closed physical summaries are not recalculated or migrated into new forecasts.

The synchronous browser CompareExchangeChecked commit compares the activity envelope and review snapshots of cardio, strength, avatar and existing target-plan stores. Any meal/plan/source change after review invalidates confirm. Same-tab stale commands also check the current day instance. Move transfers one meal and its existing entry IDs between two Open same-profile dates in one envelope CAS; destination cannot be future/closed, and the old planned-slot link is cleared. UI offers move, edit and delete while Open. All source journal guards understand schema 1 and 2. Existing restore epoch invalidates stale tabs, including byte-identical restores.

Full backup retains reference values, names/notes, grams, meal metadata, template slots, open saved records, coverage and frozen summaries byte-for-byte. Unsaved form fields are transient and must be saved first. Restore uses the same validators before mutation and existing rollback/recovery. Backups without the day store, and schema-1 backups, remain valid; migration occurs on subsequent day indexing. Schema 2 cannot be opened by the old Phase 3 reader: retain a pre-upgrade backup for binary rollback; do not downgrade the payload or discard nutrition.

## Forecast v3 boundary

`ClosedDayNutritionAggregator.Build(days, profileId, startInclusive, endExclusive, asOf)` reads only that profile/window, rejects duplicate dates, validates inputs and excludes days closed after `asOf`. The window ends no later than the date of `asOf`. Only Completed/RestDay with Complete nutrition enter averages. Open/MissingData/Partial/old unknown days do not become zeros; warnings report the count versus the entire requested window, including absent days.

Returns kcal/P/F/C averages, observed/eligible/partial/not-recorded counts, population calorie variance and warnings. A macro average is null if any eligible day lacks that macro; it never averages a selective known subset. MissingData is excluded even when draft meals exist. `ToForecastInput()` supplies the four nutrition fields only; the future caller owns horizon, activity and baseline assumptions. It neither reads nor writes ForecastSnapshot or creates a Hypothesis.

Stage A physiology is unchanged. If recorded macro energy is incompatible with its existing 5% rule, the aggregate retains exact observed macros, emits a warning, and the mapping uses calories-only fallback. Null fields preserve fixed/partial-macro fallback. Compatible complete macros enable macro TEF and carb glycogen. Explicit all-zero average is retained as evidence but cannot be mapped to Stage A's positive-intake domain. Sodium/ECF remains production NO-GO, with no input UI. Old snapshot numeric replay remains unchanged.

## Diagnostics and future providers

The existing opt-in “Дни активности и подтверждённые итоги” also exports nutrition. Allowlist: day/meal/food-entry pseudonyms, dates, category, basis, reference/actual grams, numeric label values/scaling, totals, coverage, eligibility, frozen target and kcal difference. No food names, notes, custom meal labels, raw IDs or arbitrary source strings. JavaScript diagnostics independently project scaling in double precision, using ≤1e-6 per entry comparison tolerance; they never write production totals. Full backup, unlike analysis export, intentionally retains all user data.

`INutritionReferenceProvider` is only an extension boundary. Any future saved food/recipe/database result must be copied into a versioned reference snapshot; mutable provider records cannot own historical facts. No catalog, provider calls, barcode, recipe engine, automatic advice, timing score, sodium UI, backend or network dependency is introduced.

## Evidence and limits

Tests: `NutritionTests`, `NutritionStorageTests`, `nutrition.test.mjs`, new `nutrition-smoke.cjs`; published offline smoke now logs food and verifies frozen totals after reload. The legacy suites still cover snapshot replay and Avatar/activity flows. Screenshots, measured timings and final counts: [evidence](evidence/nutrition/README.md).

No human physiology accuracy claim follows from arithmetic tests. Desktop Edge/SwiftShader and CI Chromium are covered; a physical weak phone remains unmeasured. LocalStorage CAS is the existing single-browser-task pattern, not a multi-device database. Coverage is an explicit user assertion, not automatic detection of missing meals. No reopen/amend. Hypothesis 14/30 lifecycle, CheckIn/outcomes, autonomous coach, photorealistic renderer, GeometryWarpRenderer, BodyParts3D and DeltaShape are deferred to their own phases.
