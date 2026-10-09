# Hypothesis lifecycle — Phase 5

Baseline and #24 merge: `f5244d735defaa3ead97d7c5bd356ed5b93c64b4`, ordinary merge of `55f4a365b8b4a086a84d760d4b5eaf463d15cc6d` into `0f943cc4fdd637d30d71f7d7519bb0aaf0d8e05c`. [Complete baseline CI](https://github.com/branch-danya-dev/trening/actions/runs/37892933840) passed **before** creating `codex/hypothesis-lifecycle`. History was not rewritten.

An observed hypothesis is an explicitly saved prospective claim: if the recently recorded routine continues, the frozen model predicts this body state after exactly 14 or 30 calendar days. A manual calculator scenario is not this claim. Nothing is issued automatically after closing a day.

## Ownership and persistence

`WorkoutCalculator.BodyModel.Hypotheses` owns evidence policy, aggregation, mapping, immutable origin, exact endpoint, outcome and transitions. `ObservedHypothesisStore` owns the new strict SHA-256 envelope `workoutcalc.observedHypotheses.v1` (schema 1). The suggested `workoutcalc.hypotheses.v1` key is **already occupied by legacy scenarios** and is deliberately preserved byte-for-byte. Legacy serialization, ForecastSnapshot numerical replay and the previous forecast archive are unchanged.

Each record consists of a deeply immutable `HypothesisCore`, its canonical JSON SHA-256, and append-only lifecycle events. The core embeds its complete immutable ForecastSnapshot and optional calibration revision; this is the hypothesis-specific snapshot archive, committed atomically with the origin in **one** localStorage CAS. It does not perform a separate append to `workoutcalc.forecasts.v1`: that would create a two-store torn-write boundary and expose these outcomes to the legacy scenario calibration pipeline. `Core.Forecast.Id` is the frozen forecast reference. Full backup already captures both stores independently.

The core captures profile/avatar/cycle/origin revision IDs, full current AvatarRevision, issue timestamp, local start/target dates, evidence cutoff, exact policy thresholds, closed evidence copies and hashes, evidence summary, assumptions, model/calibration versions, uncertainty and endpoint geometry descriptor. A review captures source-store guards. Issue rechecks all guards plus the hypothesis-store revision; a changed source requires review again. A review cannot cross midnight. The forecast origin/cutoff is the review time; `CreatedAt` records the actual save time. Retrying the same review/idempotency key returns the same record. Reusing a key for a different origin is rejected.

Strict read/restore rejects future schema, bad SHA, unknown members, malformed states, inconsistent endpoint/input/evidence hashes, missing Avatar revisions/cycle events, missing or modified closed days, missing or changed outcomes, and duplicate IDs or active records. Preview selects all eligible observations from its source revision and Issue guards that revision. Subsequent reads validate the frozen references; they never reconstruct set membership from later source records, which can even share a timestamp because of clock resolution/adjustment. Events cannot replace or remove the core or previous events. Read failure preserves raw bytes and blocks writes. A linked outcome BodySnapshot becomes protected from editing/deletion; other history records retain their existing behavior.

CAS has the same browser-local concurrency scope as existing stores. Backup restore epochs reject writes from stale pre-restore tabs. This is not multi-device synchronization or a tamper-proof research database.

## Evidence policy `recent-closed-1`

The window is `[max(issueDate − 14, current cycle local creation date), issueDate)`: up to **14 completed calendar days**, excluding the partially elapsed issue day. A newly locked avatar therefore needs three subsequent fully closed days; historical entries before that cycle are not borrowed to pass the gate. Only matching profile/cycle days are considered. Factual closures require `Closure.ClosedAt <= EvidenceCutoff`. MissingData requires its confirmation timestamp at or before cutoff. Later closures cannot retroactively join an old origin.

Gate:

- Active, locked Avatar; valid tracking cycle; `HypothesisResetRequired == false`.
- At least **3** factual Completed/RestDay days.
- At least **3** Complete nutrition days with **positive** daily calorie evidence.
- A valid finite v3 forecast; existing input bounds still apply (for example 500–10000 kcal/day).
- No Active/AwaitingOutcome hypothesis in that cycle.

Maturity is a policy label, not a validated accuracy probability: fewer than 3 factual or positive complete-nutrition days = Insufficient; 3–6 eligible days = Preliminary; at least 7 = ObservedRoutine. Future enum values InitialPersonalized/Personalized are not assigned in Phase 5. A complete fasting day remains genuine zero intake but does not satisfy the positive-day count. Complete nutrition averages still include genuine fasting days; Partial/unknown intake is never imputed or replaced with plan targets. Nullable macros retain the existing nutrition adapter's fallback.

The summary retains calendar coverage, Completed/RestDay/MissingData/gaps, complete/partial/unknown nutrition, average/variance, known/unknown energy, duration coverage, walking distance/steps, cardio count/known energy, strength sessions/IDs/frequency and muscle load. Empty RestDay is a behavioral observation, not zero food. MissingData is neither inactivity nor fasting.

## Activity adapter `observed-routine-pal-1`

Current engine semantics remain `BMR × ActivityFactor + explicit cardio/day + explicit strength/day`. Stage A physiology and Hall/NIDDK coefficients are unchanged.

`ClosedDayActivityAggregator` reads frozen event summaries only. Non-strength means events other than `Strength`. For each factual day it accepts the daily non-strength subtotal into the mean **only when every non-strength event has known active kcal**. An explicitly empty rest day, or a strength-only day with no non-strength events, has known zero non-strength activity. A day with any unknown non-strength energy is excluded from this mean; its known subtotal and unknown count remain in the summary. If no eligible energy days exist, average energy is null.

Mapping:

```text
startingBMR = current Mifflin calculation on the frozen Avatar base profile
ActivityFactor = clamp(1.2 + knownMeanNonStrengthActiveKcal / startingBMR, 1.2, 3)
Cardio = null; CardioPerWeek = 0
observedStrengthPerWeek = closed strength session count / factual day count × 7
StrengthPerWeek = clamp(round(observedStrengthPerWeek, away from zero), 0, 14)
StrengthTraining = StrengthPerWeek > 0
```

If mean energy is null, the adapter explicitly uses the 1.2 household assumption, retains null in assumptions and displays the limitation; it does not claim to have measured zero activity. This coefficient is not measured PAL. Clamp and rounded frequency are disclosed. Existing generic strength session duration/MET is an assumption, not observed session energy. Strength energy never enters the non-strength adjustment. Short spontaneous exercise does not become an invented full strength session; its unknown energy remains unknown. Cardio events have exactly one expenditure path: the non-strength adjustment, never `ForecastInput.Cardio` again. Automated tests compare resulting starting expenditure against baseline plus the known event's energy.

## Capability and regional program boundary

Weight/composition and composition-based future 3D can be available with limited strength evidence. V1 never manufactures a detailed program from recent exercises. Existing detailed programs belong to editable manual scenarios; selecting one for a prospective hypothesis would need a distinct reviewed program assumption. This implementation keeps `StrengthProgram=null`, regional muscle capability false, and freezes identity regional morph. It explicitly shows that limitation while permitting composition-based hypotheses. Photorealistic render eligibility is always false and no dead render CTA is added.

## Exact endpoint and reproducible geometry

`TargetDate = LocalStartDate.AddDays(HorizonDays)`; only 14 and 30 are accepted. Forecast coverage is `ceil(days / 7)` weeks. `ForecastEvaluationService.At` selects week 2 for 14d and interpolates week 4→5 at `30 / 7.0` for 30d. **30 is never replaced by 28.** Old weekly points are untouched. The separate endpoint freezes weight, fat/lean mass, BF, glycogen/water, all girths and heuristic ranges.

`composition-endpoint-1` stores the full endpoint BodyProfile JSON, frozen visual corrections, identity muscle morph, Avatar builder/fitter/asset versions and composition model version. Its canonical descriptor hash is the target geometry **input** hash, not a hash of mesh bytes. The viewer builds from those inputs and caches by descriptor hash, reusing loaded model assets. It never looks up the live Profile for this endpoint. Phase 7 owns persisted mesh artifacts/versioned future renderer migration; no mutable mesh pointer is stored now. Numerical endpoint and descriptor replay are exact; cross-platform floating-point mesh byte identity is not claimed.

## Uncertainty `evidence-range-1`

Existing expected ranges remain heuristic. Only the separate hypothesis endpoint widens them; saved weekly ForecastSnapshot points remain identical:

```text
multiplier = min(2.5,
    (Preliminary ? 1.5 : 1)
    + 0.5 × gapDays / calendarWindowDays
    + 0.25 × unknownNonStrengthEnergyEvents / max(1, allEvents)
    + 0.25 × (partialNutritionDays + unknownNutritionDays) / calendarWindowDays)
```

The half-width of each weight/girth range is multiplied around its fixed expected value, lower bounded at .01. Composition-only weekly forecasts have no girth ranges; the endpoint adds a separately versioned half-width in cm of `.5 + .25 × sqrt(weeks) + .05 × weeks`, then applies the same multiplier. This is an explicit conservative display heuristic, **not** a confidence interval or empirical coverage claim. Structured limitations retain unknown macros/energy, gaps, incomplete nutrition, household/strength assumptions, unavailable regional forecast and unvalidated individual accuracy.

## State machine and outcome `target-minus1-plus3-1`

```text
Active → AwaitingOutcome → Evaluated
   ├───────────────→ Evaluated (target − 1 allowed)
   ├───────────────→ Cancelled (explicit, before target only)
   └→ ArchivedByRecalibration
AwaitingOutcome → ExpiredWithoutOutcome (calendar date > target + 3)
AwaitingOutcome → ArchivedByRecalibration
```

One Active/AwaitingOutcome per cycle. Terminal hypotheses remain visible in Progress/history; later hypotheses use a new recent window. Calendar transitions reconcile when the panel is opened/refreshed; no background timer is needed. Manual recalibration immediately attempts archival and leaves a durable Avatar cycle event for idempotent reconciliation after interruption. The validated append-only cycle order handles actions sharing a timestamp. Same-cycle factual/photo revisions preserve the issued origin.

Preferred observation is exact target date; accepted observation and recording window is target−1 through target+3 inclusive. Weight is mandatory. The user creates a manual BodySnapshot through existing Progress UI, or chooses an existing factual record and confirms independence. Optional BF/girths are compared only when recorded, without zeros or profile-filled estimates. Early observations compare at their actual elapsed day. Late observations compare to the frozen target endpoint, carry LateOutcome and are ineligible for automatic calibration. Comparison shows baseline/personalized prediction, actual, signed/absolute errors, timing, source and exclusions; no causal attribution is made.

No outcome after grace means ExpiredWithoutOutcome with no accuracy or calibration sample. Cancellation/recalibration are also not model failures. A historical fact entered after grace cannot silently reopen an expired hypothesis.

## Calibration boundary

Hypothesis calibration uses the existing conservative `ForecastCalibrationService`, but feeds only previously Evaluated hypotheses with eligible outcomes in the **same cycle** and same composition version, with `RecordedAt <= new cutoff`. Early 13-day outcomes remain excluded by the existing minimum 14-day calibration rule. Late, cancelled, expired and recalibration-archived origins provide no samples. No legacy scenario facts are automatically mixed into this prospective partition. The revision (including the baseline result when evidence remains insufficient) is captured at preview cutoff and frozen with the next issue. One eligible outcome is retained as evidence but cannot override the existing four-week-group minimum. Later calibration never updates old numeric points.

## UI, backup, export and validation

Plan contains the eligibility/review/save workflow above the explicitly named manual Scenarios. Progress contains historical hypotheses and basic Forecast vs Fact alongside existing measurement history. The review shows evidence counts, diet/strength/household assumptions, exact date, ranges, warnings and composition 3D. The 3D rebuild is explicit and cached, not triggered by every nutrition edit or render.

Full backup includes the new envelope, embedded ForecastSnapshots/calibration, all origin evidence and events, linked body facts and Avatar revisions. Strict cross-reference validation runs before restore mutation. Old backups without the key remain valid; old scenarios are never migrated into observed evidence. `hypotheses` is a separate opt-in validation section with export-local pseudonyms, counts/coverage, frozen averages, model/calibration metadata, endpoint/ranges, state and known outcome errors/exclusions. It omits raw IDs, source hashes, free text, notes, photo blobs and raw input JSON.

`HypothesisLifecycleTests`, JS export/backup tests and `hypothesis-smoke.cjs` cover gates, exact endpoints, frozen origins, activity mapping, outcome/calibration boundaries, CAS, corruption, restore and mobile widths. Browser evidence/performance is in `docs/evidence/hypothesis`. Final CI status and counts belong in that report and PR. No claim about physical low-end phone performance or validated individual prediction accuracy follows from these checks.

## Deferred

Phase 6 automatic photo CheckIn, front/side/back quality/reconstruction and automatic Avatar update; Phase 7 GeometryWarpRenderer/mesh artifact storage; Phase 8 managed photorealistic rendering/API; food catalogs/barcodes/recipes; autonomous coaching; BodyParts3D; DeltaShape; sodium/ECF production UI; claims of validated individual accuracy. Regional program selection for hypotheses is explicitly limited as described above.

## Phase 6 — Current Avatar check-ins (2026-10-09)

Current implementation: [CHECKIN_LIFECYCLE](CHECKIN_LIFECYCLE.md). Baseline #25 was merged with ordinary merge commit `106efdaf28cdb7fdebb91b8b8bf2c98dd254f2a7`; full [main CI](https://github.com/branch-danya-dev/trening/actions/runs/37899920321) passed before `codex/avatar-checkins` was created. Separate factual observations update the current avatar automatically after the versioned gate while preserving the cycle origin and every issued forecast. Manual recalibration remains separate. Partial/photo-only facts, prior/correction merge, source quality/conflicts, idempotency, recovery and target-hypothesis integration are specified in the contract. Phase 7/8 are deferred. Validation results and limitations: [Check-in evidence](evidence/checkin/README.md). The implementation PR is not auto-merged.
