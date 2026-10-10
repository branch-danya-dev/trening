# Pre-UI application contract, version 1

Baseline: `355cebe21f69dda43a5548a2be6556e01e906312` (PR #30, ordinary merge). This contract freezes semantics and boundaries; it does not freeze markup, navigation, screen composition or CSS. No backend is involved.

## Commands and ownership

All names below refer to existing public application/service APIs, not endpoints. Components bind/edit temporary input and display results. They must not parse browser envelopes, write factual keys, construct transaction payloads, recompute frozen hashes, or advance a lifecycle by changing DTO properties. `IJournalStorage` is infrastructure, not a UI API.

| Flow | Stable boundary | Outcome / constraints |
|---|---|---|
| Profile / onboarding | `AvatarDomainStore.Initialize`, `UpdatePreferences`, `UpdateCalculationSettings`; `AvatarLifecycle.Create` and `AvatarBuilder.Capture` | Initialization explicit/idempotent. Profile and active-avatar pointer committed together. Legacy migration is explicit. |
| Avatar draft/review/lock | `AvatarDomainStore.EditDraft`, `Confirm`, `StartRecalibration`, `CancelRecalibration`, `AcknowledgeNewCycle`; `AvatarBuilder` reconstruction | Draft is editable; revisions are immutable. Recalibration creates a new tracking boundary. Factual check-in stays within its cycle. |
| Today/calendar | `ActivityDayStore.Index`, `Find`, `Ensure`, `Review`, `Close`, `Missing`, `SavePlan`, `SaveEvent`, `RemoveEvent` | `Index`/`Ensure` are commands and can create an open day. `Find` is read-only. Closed days and frozen summaries are protected. Pass original review guards through confirmation. |
| Nutrition | `ActivityDayStore.SaveMeal`, `RemoveMeal`, `MoveMeal`, `Review`, `Close`; `NutritionSummary.Build` | Meals own immutable reference nutrients/quantities. Partial, complete and explicitly no-food are distinct. Plans never become intake facts. |
| Strength/cardio | `StrengthJournalStore` and existing guarded cardio journal boundary; `ActivityDayStore.Index` links sources | Preserve source IDs, provenance and closed-day source-write guards. Do not duplicate a linked workout as a manual event. |
| Hypothesis preview/issue/history | `ObservedHypothesisStore.Preview`, `Issue`, `Synchronize`, lifecycle commands; `HypothesisService` policies | Preview does not persist. Issue is guarded by exact source bytes and idempotency key. Frozen core/endpoint/forecast never change during evaluation or expiry. |
| Check-in | `CheckInApplication.Begin`, `Resume`, `AttachOutcome`, `Load`, `OutcomeAttached` | Application owns durable Draft → Ready → terminal orchestration and candidate selection. `Resume` receives a photo gateway; no-photo commands never invoke it. Do not split the factual commit across components. |
| Forecast vs fact | `ObservedHypothesisStore.Evaluate` via `CheckInApplication`; `Hypothesis.Outcome.Rows` / frozen snapshot readers | Attach is explicit and idempotent. Outcome window, source quality, fit-vs-independent-validation distinction and calibration eligibility are semantic. Never rerun today's model to replace a historical endpoint. |
| GeometryWarp | existing `GeometryWarpRenderer` / `geometry-warp.js`, `render-store.js`, versioned render contracts | Local eligibility first: exact origin photo/revision, asset/version/hash, supported device, quality gates. Synthetic pixels cannot become observations. `savedResults` is a history query; `readArtifact(id)` is keyed and validates provenance. |
| Backup/restore | `BackupValidation`; JS `exportBackup`, `inspectInput`, `preparedLocal`, `preparedRenders`, `restorePrepared` | ZIP validation plus .NET domain/reference validation precedes replacement. Recovery journal and generation protect all stores and stale tabs. Reads of an interrupted check-in remain blocked until recovery. |
| Validation export | `ValidationPanel` report assembly and `validation.js/exportValidation` | Explicit section opt-ins; allowlisted pseudonymous analysis. Images require separate explicit permission. Usability diagnostics require both local recording and export opt-ins. No upload. |

The current Home partials still coordinate rendering, input binding and refresh notifications. These are presentation glue, not alternative factual writers. Check-in orchestration was extracted because it owned a durable state machine. Do not turn this targeted separation into a wholesale domain rewrite during UI work.

## Read models

`ProductReadModels.Read(today)` returns `ApplicationOutcome<ProductSummary>`: `TodaySummary`, `AvatarSummary`, `ProgressSummary`, `HypothesisSummary[]`, `CheckInSummary[]`, and capability policy. Date is supplied explicitly. This projection never persists migrations, creates days, advances hypotheses or rebuilds geometry. It preserves unknown values instead of substituting zero.

For actual loaded asset availability, use `RuntimeCapabilities.Current`. It feeds validated MakeHuman/atlas/anatomy facts and the explicit session research opt-in to `RuntimeModelCapabilities`, the single policy authority. `ModelRegistry` remains the frozen provenance authority; its historical manifests are not rewritten by runtime policy.

`StorageHealthService.Read` exposes browser availability, persistence granted (nullable), estimated origin usage/quota (nullable), approximate localStorage bytes, pending recovery and backup recommendation. `navigator.storage.estimate()` does not describe the separate localStorage ceiling. No successful backup date is currently tracked: `LastSuccessfulBackup=null`, `LastBackupTracked=false`. Do not invent a timestamp or interpret unavailable estimates as zero.

Use the existing validated stores for detail/history pages. Profile/date lookup is indexed in ActivityDayStore; hypothesis/outcome and check-in cross-references use derived dictionaries; factual/revision hashes and intrinsic checks reuse only immutable record identities. Photo sessions/images and render artifacts have keyed IndexedDB reads. Full archive/history listings are intentionally linear cold paths. No index is authoritative or part of backup.

## Stable error taxonomy

`ApplicationErrorCode` and `ApplicationError(Code, Message, Field)` are the UI-facing categories. `ApplicationOutcome<T>` distinguishes success from failure. `ApplicationCommand.Run(() => store.Command(...))` adapts legacy string-returning commands and captures categories at failure sites. Catch thrown errors with `ApplicationError.From`; browser errors carry explicit `[Code]` tokens and a JS `code` property. Never inspect Russian phrases. Existing fallback messages remain usable.

| Code | UI behavior to preserve |
|---|---|
| `Validation` | Keep draft; let the user correct input. `Field` is optional; do not guess an input field from message text. |
| `StaleConflict` | Keep draft, reject the write; reload/review fresh state before retrying. Do not silently overwrite. |
| `CorruptOrFutureSchema` | Preserve raw bytes; block writes; allow backup/recovery. Do not treat failed load as an empty store. |
| `MissingAsset`, `UnsupportedCapability`, `ResearchDisabled` | Show capability/reason; preserve historical snapshot and last known good geometry. |
| `InsufficientEvidence` | Keep partial information and exact eligibility reasons; never fabricate a forecast. |
| `PhotoQualityConflict` | Retain permissible manual facts; do not overwrite avatar with rejected photo evidence. Check-in quality has finer `CheckInReason` codes. |
| `StorageQuota` | Failed transaction rolls back to the complete previous factual state; offer export/removal of derived artifacts. A durable draft may already exist. A second failure during rollback becomes `RecoveryRequired`, retaining the journal and blocking ordinary access until recovery. |
| `UnsupportedBrowser` | Explain unsupported storage/locks/device capability; do not claim persistence succeeded. |
| `RecoveryRequired` | Resume recovery before normal writes; never discard the pending marker to dismiss an error. |
| `Unexpected` | Preserve data and draft, report fallback diagnostic; no automatic destructive repair. |

Legacy command strings that represent generic input/state rejection still map to `Validation`; exact domain reason enums remain available for photo, hypothesis and renderer decisions. The next UI must consume those enums, not attempt to translate every historical string into a new inferred category.

## Storage and integrity contract

| Authority | Format/version | Index/query |
|---|---|---|
| `workoutcalc.avatarDomain.v1` | schema 1, profile/avatar/revisions/cycle | Current pointers; immutable revision identity |
| `workoutcalc.bodySnapshots.v1` | schema 1, facts/import references | Validated snapshot cache; derived keyed reference maps |
| `workoutcalc.activityDays.v1` | envelope schema 1 reader / schema 2 writer | `(profile,date)` map, grouped workout sources; explicit CAS migration |
| `workoutcalc.observedHypotheses.v1` | envelope schema 1, canonical frozen core hashes | Derived ID/outcome maps; historical readers retained |
| `workoutcalc.checkIns.v1` | envelope schema 1, durable events | Validated immutable records; exact source guards |
| Legacy forecast/strength/cardio keys | Existing versioned readers retained | No reclassification as new prospective hypotheses |
| `body3d-photos` | IndexedDB 3: sessions/images/settings/renderArtifacts | Primary keys; atomic photo/metadata and source-delete cascades |
| `trening-recovery` | IndexedDB 1, recovery store | Separate `pending` restore and `checkin` WAL keys |
| `trening:checkin-transaction-v1` | Marker envelope 1 (inline) or 2 (IDB pointer); WAL payloads 1 and 2 readable | SHA-256, exact commit CAS; v2 readonly dependency hashes; before-image for quota rollback |
| `trening:data-generation` / restore lock | Existing generation/guard | Invalidate derived reads after restore, reject stale tabs |

Caches retain at most two exact raw documents per immutable DTO type, plus weak record/reference caches. The JS bridge keeps at most two read handles per key (32 keys maximum); a missing/evicted handle fails CAS and retries. Raw bytes remain authoritative: handles never replace exact comparison. Cached reference checks include all source raw bytes or all relevant immutable source identities plus generation. Failed validation is never cached. Mutable legacy workout DTOs are not identity-cached.

New writes use compact UTF-8-friendly JSON escaping only in storage contexts. Canonical domain hash serialization is unchanged. Old escaped envelopes remain readable. Do not change canonical JSON, frozen version strings, source quality, date/cutoff semantics, idempotency keys, cycle pointers or provenance during UI refactoring.

The check-in transaction acquires the existing archive Web Lock. It hashes the WAL, rechecks exact source bytes, stages durable recovery, then commits without asynchronous gaps. If inline WAL storage is full, it stages the same SHA-256-checked payload in IndexedDB, rechecks CAS after that await, and installs a small marker before factual writes. Other interruptions retain roll-forward recovery; quota failures roll back the complete before-image. Restore keeps its previous rollback semantics. Synchronous IndexedDB request failures explicitly abort photo/PIN/restore transactions.

## Safe presentation changes and deferred work

Safe: layout, colors, typography, navigation grouping, labels and help text, screen composition, charts, loading affordances, read-model composition, screenshot baselines and accessible names together with their behavioral tests. Preserve stable semantic IDs/roles documented in TESTING.md.

Not presentation: facts vs plans, unknown vs zero, calibration eligibility, consent/export choices, immutable closures/endpoints, provider selection policy, transaction/recovery/CAS/version behavior, photo provenance and research access.

PWA updates wait until every old client closes. An open draft must not be automatically reloaded by another tab or by controller-change handlers. Offline cache is tied to the build manifest; the version-transition browser suite covers both clients and both generations.

Developer fixtures are generated by `WorkoutCalculator.PreUiAudit` and loaded only from Node/Playwright `fixture-loader.cjs` with explicit `enabled:true`, a new isolated context and loopback URL. The loader sets sessionStorage `trening:dev-fixtures=enabled`. Backups made in this mode carry `syntheticFixture:true`; validation and restore reject them outside that explicit loopback mode. This prevents accidental import, not malicious archive relabeling. Research opt-in is separate and remains off. Geometry scenarios use the existing frozen geometry generator. No loader, seed menu or fixture JSON is published in wwwroot. These records are synthetic testing material, never validation evidence about people.
