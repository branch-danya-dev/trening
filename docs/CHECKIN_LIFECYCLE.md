# Current Avatar check-ins — Phase 6

Baseline: #25 merged with ordinary merge commit `106efdaf28cdb7fdebb91b8b8bf2c98dd254f2a7`; [complete main CI](https://github.com/branch-danya-dev/trening/actions/runs/37899920321) passed before `codex/avatar-checkins` was created. No squash/rebase. The Phase 6 PR is for review, with auto-merge disabled.

## Ownership and states

`BodyCheckIn` is the observation envelope, not the forecast or mesh. Schema 1 / `current-avatar-checkin-1` freezes profile/avatar/cycle/base-revision IDs, local observed date, recording timestamp, manually entered nullable values, optional local photo reference, compact analysis provenance, optional candidate hypothesis ID, lifecycle events and terminal results. Stable fact/revision IDs derive from the check-in ID and purpose; retries of identical versioned inputs do not invent another identity.

```text
Draft (user submitted facts; recoverable photo-analysis job)
  → ReadyForProcessing (analysis frozen)
  → ProcessedAccepted (fact + current revision)
  → ObservationOnly (reference/photo envelope; no supported numeric fact)
  → RejectedForAvatarUpdate (observation retained; last-known-good avatar retained)
  → Failed (technical reconstruction failure; observation/facts retained)
```

Draft analysis may be enriched only for the same photo/session/date/source/profile. Ready and terminal observation inputs are immutable. Terminal events are append-only; there is no edit/delete command for accepted observations. Cancellation is reserved, not exposed as a terminal processing action in v1. Closing an unsubmitted form creates no observation.

`BodySnapshot.Validate()` is unchanged: an empty snapshot is still invalid. Weight is recommended, not globally required. Weight-only, BF-only and girth-only facts are possible. No preceding weight, BF, girth, sex, height, age or shape is copied into the new snapshot. System profile supplies sex/height to reconstruction only. Photo-only front/side analysis can create a partial snapshot from supported waist/hip estimates; reference-only, unusable or unsupported photos remain in the envelope without a fake snapshot.

The legacy runtime/calculator projection also starts from the active revision and persistent profile, then overlays known facts; a partial snapshot must not reset sex/height/age to demographic defaults. This projection is not persisted as a measured snapshot and supplies no new outcome fields.

## Sources and photos

- Manual numeric values are factual and override lower-priority photo estimates in the same observation.
- `PhotoDerived` waist/hips carry the existing ANSUR regression RMSE and session provenance. Unsupported body measurements are never invented.
- `AvatarDerived` metrics describe the mesh and never enter `BodySnapshot` or hypothesis outcome rows.
- A source-rejected photo-only envelope is retained for diagnostics with `ObservationAccepted=false`; it is never accepted as a factual observation. Valid manual facts alongside a rejected source remain accepted independently.
- Original observation + explicit user confirmation of person/date/source are required. Known Generated/Synthetic/Unknown domain sources fail the factual-photo gate; known Generated/Synthetic session metadata cannot be overridden by the checkbox. Arbitrary user uploads cannot be forensically certified as original; this is a declared provenance boundary, not an AI-image detector.
- Back photos are local visual-history references only. They are excluded from silhouette analysis, measurement inference, photo quality pair requirements and forecast overlays. Front and side remain the supported pair.
- Photos stay in the existing local IndexedDB store. With PIN enabled, image blobs/previews use the existing AES-GCM policy; metadata/analysis remain local plaintext as before. No upload or backend is added. Check-in metadata contains no raw blobs, filenames, notes or encryption keys.
- `local-user-managed-1` is the retention boundary. Explicit photo deletion may remove imagery while the frozen observation and measurements survive, as in existing body history. Backup contains all retained photos and encryption settings; deleted images are not recreated.

## Reconstruction merge, precisely

1. Find the immutable `BaseAvatarRevisionId`. Start from its **uncorrected frozen base profile**, not its already corrected mesh and not demographic defaults.
2. Mark absent measurement fields as `VisualEstimate`. Prior facts constrain prior shape, but are never relabeled as newly observed facts.
3. Overlay only this observation's manual weight/BF/girths. An accepted front/side estimate fills only a girth not manually entered in this observation. Conflicting photo values remain diagnostics, not snapshot values.
4. Retain the versioned correction profile once. `CorrectionProtectedPriors` carries which prior girth targets were protected against fullness correction. This prevents an omitted remeasurement from suddenly enabling an old v2 correction that was previously suppressed by a factual girth. Legacy v1 applied fullness even to factual girths, so these old corrections remain applied until a new measurement overrides them. The mask is geometry metadata, not measurement provenance. Newly measured girths are protected in either version.
5. Run the existing deterministic AvatarBuilder/fitter with cached assets/baseline; compute final mesh metrics and residuals. No incremental deformation or cumulative correction delta is applied.
6. Apply the versioned gate. If accepted, append a `FactualUpdate` revision linked to this check-in and move only `ActiveRevisionId`. Otherwise retain the old active revision byte-for-byte.

The nullable protection metadata is omitted for historical inputs and endpoint descriptors. Old serialized forecast/revision hashes and replay stay unchanged. New hypothesis descriptors freeze the protection metadata when present; physiology is unchanged. Legacy correction/fitter versions remain supported by their existing replay path; unsafe residuals reject the new solve.

## CheckInQualityPolicy v2

`checkin-quality-2` / `checkin-front-side-1` are engineering policy thresholds, **not calibrated probabilities of correctness**.

| Check | v1 rule |
|---|---|
| Pair | front + side if either is used for reconstruction; back excluded |
| Silhouette | correct view, positive dimensions/scale, 33 pose points, ≥30 usable levels per view, finite measurements |
| Pose/source confidence | minimum hip/shoulder visibility ≥0.8; valid [0,1] range |
| Image warnings | none for accepted reconstruction |
| Scale/profile | same sex and height within 0.1 cm; reconstructed image stature within 3% of system height |
| Observation date | photo date matches check-in date; no future observations |
| Manual/photo conflict | difference > max(5 cm, 3 × existing photo RMSE) rejects photo/geometry; manual fact retained |
| Abrupt weight | difference from prior body input > max(10 kg, 15%) rejects geometry pending verification |
| Photo/prior geometry | unmeasured photo girth differs from previous mesh by >25 cm: reject geometry |
| Fitter | no tissue limit, no missing mesh girths, maximum fitter residual ≤12 cm |
| Known values | manual girth residual ≤3 cm; accepted photo girth residual ≤max(3 cm, 2 × RMSE) |
| Current state | expected active revision/cycle/status; older observed date never replaces current geometry |

These conservative checks may retain an old avatar for a valid body change. The user can correct measurements, retake photos or separately recalibrate. There is no accuracy claim. Weight change diagnostics compare to a shape prior which may itself be estimated; they are not automatically a measured weight change.

Manual/photo conflict still attempts a solve against manual constraints for diagnostics, but the conflicting result cannot become active. Example message: «Замеры сохранены, но фото не использовано для обновления аватара: данные противоречат друг другу.» Low-quality observations are rejected for geometry, not called technical failures.

## Current state vs recalibration and hypotheses

Check-ins preserve `TrackingOriginRevisionId`, `TrackingCycleId`, cycle events and reset flags. Manual «Исправить аватар вручную» opens the existing isolated recalibration session; confirmation creates a new origin/cycle and archives open hypotheses through Phase 5.

Issued hypothesis origin, evidence, embedded ForecastSnapshot, exact 14/30 endpoint descriptor/hash and previous evaluation are never recomputed after a check-in. An open hypothesis in its existing −1/+3 outcome window can be a candidate link. Recording the observation commits first. Only then does the user explicitly select «Использовать для проверки гипотезы»; existing `ObservedHypothesisStore.Evaluate` performs evaluation. A crash before this optional step leaves a valid observation and a repeatable action, not a partially linked hypothesis.

Existing weight requirement for hypothesis evaluation, source weighting, known-fields-only rows, late/expired/calibration exclusions and model partitions remain. A bad photo cannot invalidate a good manual weight: rejected photo estimates do not enter its manual snapshot. Photo-derived outcomes retain their existing quality/RMSE weighting. Repeated confirmation of the same link is idempotent; a different evaluated outcome cannot be overwritten. Evaluation of the same-day girths used to fit a new current avatar compares with the **previously issued independent forecast**, not the fitted current mesh.

## Storage and recovery protocol

`workoutcalc.checkIns.v1`: strict source-generated schema, SHA-256 envelope, explicit ID/source/cycle/fact/revision references, unique check-in and photo-session ingestion. Old archives without CheckIns remain valid unless they contain orphan Phase 6 revisions.

1. Persist user-submitted Draft, then enrich analysis and persist Ready. Reload offers to continue the same job with its captured manual fields, source and base revision.
2. Prepare and validate immutable observation, BodySnapshot and Avatar-domain after-images against exact before-images. Validate existing hypothesis references without changing their payload.
3. `commitCheckIn` obtains the same `trening-archive` Web Lock as backup/restore. It checks restore generation and exact expected stores immediately before staging.
4. One `trening:checkin-transaction-v1` write stores version/checksum/epoch/before/after images. This is outside exported `workoutcalc.*` keys. Then synchronously write CheckIn → BodySnapshot (if any) → Avatar (if any); remove the journal last. No await between staging and these writes.
5. A pending journal blocks shared ordinary writers and strict reads. At startup, export or restore, while holding the archive lock, recovery validates checksum, allowed keys, epoch and every before/after value. It rolls forward the exact prepared payloads and clears the journal. A mismatch preserves the journal and blocks writing instead of guessing.
6. Hypothesis attachment is separate, explicit and idempotent after factual commit. No hypothesis row can refer to an uncommitted fact.

Failure before journal staging changes no domain store. Failure after any write retains a durable payload for completing all stores. If quota remains exhausted, recovery stays blocked until space is available; no partial state is presented as complete. Fault injection covers every boundary, repeated recovery, source changes, corrupted journal, quota and restored identical bytes.

Two concurrent jobs may analyze from the same base. Once a newer job or manual recalibration wins, an old prepared commit fails CAS. Retrying against current data saves the old observation with SupersededRevision/CycleChanged and no new active revision. The original base remains frozen. These are local browser protections, consistent with existing store guards, not a multi-device database protocol. Arbitrary external writes/old incompatible clients are outside the supported writer contract.

Full restore runs both domain and cross-reference validation before replacing anything. Check-in facts are protected from generic history editing/deletion, including compare guards against a concurrent new reference. Restore preserves exact envelope bytes, revision/cycle IDs, rejected decisions, frozen hypothesis endpoint and evaluation; no reload ingestion.

## UI, diagnostics and validation data

Progress hosts «Новый замер» with empty numeric inputs and optional front/side/back. Model/Profile and the target-hypothesis prompt lead to the same flow. It contains no shape sliders and no per-vertex approval. Accepted geometry updates automatically; result and history explain saved facts, sources, warnings and whether current avatar changed. Internal IDs are absent from the normal check-in/revision UI.

Opt-in «Check-ins / наблюдения прогресса» exports pseudonymized check-in/revision/hypothesis references, dates, manual values, photo estimates/RMSE, source quality, policy decisions, previous mesh vs manual, photo vs manual, fit residuals and mesh change. Raw photos/names/free text/local IDs/hashes/PIN/keys are omitted. `UsedAsFitConstraint` and `IndependentValidationOfNewFit=false` prevent interpreting fitted same-day measurements as held-out accuracy. No prospective accuracy figure is claimed.

The new `checkin-smoke` covers manual, accepted pair, bad/conflicting photos, back-only, target-date outcome, immutable endpoint, exact reload/clean restore and 320/390/1400 layouts. All existing suites remain in CI. Offline operation uses local code/stores/assets; first-use MediaPipe assets follow the existing runtime-cache policy and need to have been downloaded before going offline. Deterministic silhouette fixtures validate plumbing/reconstruction, not real-world photo accuracy. Desktop Edge/Chromium automation does not validate a physical low-end phone.

Deferred: Phase 7 GeometryWarpRenderer, Phase 8 AI renderer/API, backend upload, daily photo requirements, autonomous coaching, BodyParts3D/DeltaShape and empirical accuracy claims.
