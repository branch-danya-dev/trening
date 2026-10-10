# Pre-UI stabilization readiness

PRE_UI_BASELINE = `355cebe21f69dda43a5548a2be6556e01e906312`

PR #30 head `bfe195084be9a3d20f620295d2ef4dc5736337d4` was rechecked GREEN/mergeable and merged with an ordinary merge commit. [Full baseline main CI](https://github.com/branch-danya-dev/trening/actions/runs/38042605807) passed before `codex/pre-ui-stabilization` was created. No squash/rebase/history rewrite.

FINAL_IMPLEMENTATION_SHA = `3be42a345ffee496575f111fec95e8264177f5e9`

[Full implementation CI](https://github.com/branch-danya-dev/trening/actions/runs/38047271063) is GREEN. Subsequent changes only finalize these reports/evidence; a reporting-only commit cannot contain its own SHA. [PR #31](https://github.com/branch-danya-dev/trening/pull/31) records its exact final head and required checks. The source/test/tool tree fingerprints in [CI verification](evidence/pre-ui/ci-verification.json) distinguish the tested code from reporting-only changes. PR #31 remains open and must not be automatically merged.

## Changes and boundaries

- One typed runtime capability policy, driven by frozen model provenance plus validated asset availability and explicit session research opt-in. Asset presence alone cannot enable research.
- Exact-raw validated caches, immutable record/hash reuse, dictionary reference joins, grouped activity sources/date lookup and no-photo CheckIn application orchestration. Canonical historical hash functions and semantic model versions unchanged.
- Bounded browser read handles retain exact CAS semantics without echoing entire readonly histories through WASM. Compact JSON storage encoding; legacy encodings remain readable.
- SHA-256-checked compact recovery WAL; IndexedDB overflow journal when localStorage cannot hold a second full archive. Quota rollback and explicit photo/PIN/restore transaction aborts.
- Typed error adapter/bridge codes, read models and StorageHealth; local diagnostics disabled until opt-in, a separate export opt-in, no remote analytics.
- Semantic test hooks/helpers, targeted keyboard/focus/label assertions. PWA updates wait for all old clients to close.
- Synthetic fixture loader outside production, mixed full-lifecycle backup, five pilot preparation drafts. [Frozen UI contract](PRE_UI_CONTRACT.md); [testing policy](TESTING.md).

No UI redesign, navigation/layout/colors/typography decisions, recruitment, data-access submission, training, backend, accounts, cloud sync or AI renderer was performed. UI changes are limited to diagnostic opt-ins/capability output, safe update notice, accessibility and test hooks. No historical reader or explicit legacy flow was deleted. Removed only the unsafe forced service-worker activation/reload path; the version-transition suite covers its replacement.

## Capability/default matrix

| Capability | Runtime status | Default |
|---|---|---|
| Procedural global shape | Production, validated MakeHuman asset required | Yes |
| Procedural muscle fields | Production, validated atlas required | Yes |
| Composition v3 / avatar reconstruction | Existing structural/reference GO / controlled testing; semantic versions frozen | Existing behavior |
| BodyParts3D | Research-only; explicit session flag plus validated sidecar/atlas/body required | NO-GO, off |
| DeltaShape | BLOCKED_PENDING_DATA_ACCESS, no artifact/provider activated | Off |
| Pseudo-DXA | BLOCKED_PREREQUISITE_AND_RIGHTS | Off |
| GeometryWarp | Local production capability, existing per-request eligibility and WebGL gates | Available when eligible |
| Managed/photorealistic AI renderer | DEFERRED; no implementation/network endpoint | Off |

Research opt-in is sessionStorage `trening:research-mode=enabled`, explicitly set by research tests. It is not an ordinary setting, navigation choice or backup field. Runtime policy cannot change frozen historical provenance. Missing pinned research assets preserve the historical record and report replay unavailability.

## Storage/version and recovery

Full matrix: [PRE_UI_CONTRACT](PRE_UI_CONTRACT.md#storage-and-integrity-contract). Avatar/fact/check-in/hypothesis schemas remain 1; ActivityDay readers preserve schema 1 migration and write schema 2 only through explicit CAS. Photos remain IndexedDB 3. Recovery DB remains version 1 with separate restore/check-in keys. New check-in WAL payload version 2 retains version 1 recovery support. Nothing makes a derived cache/index a backup authority.

The canonical `docs/evidence/pre-ui/pre-ui-full-lifecycle.zip` is **entirely synthetic**, with public fixture PIN `1234`. It covers profile, locked avatar/revisions/cycle, ActivityDays/nutrition, strength/cardio references, evaluated and expired hypotheses, check-in/fact, encrypted photos and GeometryWarp artifacts. Its `syntheticFixture` manifest flag requires explicit local developer fixture mode; ordinary production import is rejected. Exact clean restore, repeated reload/no extra migration, navigation/index rebuild, old-tab generation rejection, frozen hypothesis core and disabled research are checked. [Mixed archive report](evidence/pre-ui/mixed-backup.json); [reproduction and archive fingerprint](evidence/pre-ui/README.md).

Fault tests cover every inline/external WAL boundary, old journal compatibility, stale exact bytes/generation (including a writer during the IndexedDB staging await), failed staging, failed factual writes with complete rollback, a second failure during rollback with retained recoverable WAL, corrupt journal blocking, synchronous photo blob quota after metadata enqueue, and export/delete after quota. Physical disk exhaustion or browser eviction is not simulated by these deterministic injected failures.

## Performance findings

Root cause: CheckIn repeatedly decoded/hashed/revalidated the same full stores; reference checks used nested searches and reserialized unchanged revisions; rendering each history row reloaded hypotheses; each WASM commit copied full readonly expected histories; inline recovery duplicated those histories again. Dense archives exposed large CPU and capacity costs.

Matched dense stress archive, same original bytes/browser: initial CheckIn visible save was 1.152 / 6.419 / 87.959 s at 0 / 30 / 180 days. After the first cache/reference/bridge fixes the same archives measured 0.852 / 1.555 / 4.196 s; 365 days completed in 6.582 s. The original 365-day run did not complete within the configured timeout; no successful baseline timing is claimed. Subsequent immutable record reuse improves the final representative curve below. The dense 1000-day archive exceeds typical localStorage capacity and is not presented as a successful capacity test.

Final representative synthetic archive: one closed day/meal and factual snapshot per day, CheckIn/revision every 28 days, hypotheses every 180 days plus a recent one. At 1000 days: 35 historical check-ins, six hypotheses, approximately 4.64 million stored characters. This is a declared bounded workload, not a promise that every 1000-day record density fits localStorage.

Local Edge 154, desktop, mobile viewport, Release WASM; single-run observations in milliseconds (CI independently records its curve):

| Operation | Fresh | 30d | 180d | 365d | 1000d |
|---|---:|---:|---:|---:|---:|
| Startup/model ready | 2532 | 2937 | 4009 | 6662 | 11388 |
| Activity/calendar open | 117 | 214 | 646 | 1214 | 3261 |
| Switch calendar day | 56 | 19 | 34 | 53 | 95 |
| Add meal, form through completion | 151 | 153 | 549 | 1083 | 2778 |
| Add activity event | 50 | 135 | 550 | 1069 | 2725 |
| Close Day, review through confirmation | 103 | 214 | 767 | 1433 | 3643 |
| Hypothesis open/preview | 400 | 565 | 618 | 902 | 1839 |
| CheckIn open after preview | 95 | 313 | 409 | 542 | 1033 |
| CheckIn save, visible completion | 874 | 1265 | 1680 | 2244 | 3957 |
| Expand check-in history | 7 | 13 | 20 | 33 | 188 |
| Full backup creation/download | 138 | 88 | 90 | 108 | 158 |
| ZIP structure/checksum validation in browser | 1 | 1 | 4 | 13 | 20 |
| Full restore inspection, including domain/references | 79 | 316 | 1058 | 2026 | 5491 |

1000/fresh save = **4.53x**, 1000/365 = **1.76x**. Desired <=2x is **not met**. Exact blocker: versioned whole-envelope serialization/SHA and durable CAS/WAL still grow with factual archive size, and post-save calibration/current-fact projections process history. Achieving bounded independent persistence would require a separately versioned storage migration; it is not disguised as a cache change here. Current regression ceilings are 8x and 3.5x respectively, with the desired target reported separately. No integrity checks were dropped.

The independent Ubuntu/Chromium CI measured CheckIn save **1682 / 2401 / 3209 / 4472 / 8193 ms**, respectively: **4.87x** large/fresh and **1.83x** large/365. All measured startup/activity/meal/event/close/hypothesis/backup/restore phases are in `ci-verification.json`. In particular, 1000-day startup was 23275 ms and full restore inspection 11051 ms on that runner. The regression ratios pass; absolute desktop latency is not a phone SLA and the 2x goal is not claimed. The existing local CheckIn smoke's six save paths measured 563–1197 ms, with zero browser errors and unchanged endpoint/recovery/export assertions (`checkin-regression.json`).

An additional final-path breakdown at 1000 days: begin 308 ms, ready 265, avatar/fact validated reads 4/6, reconstruction 232, encoding 220, hypothesis checks 129, CAS bridge/WAL 335, reconstruction/commit 1750 end to end, visible save 4010. Photo analysis is absent from this no-photo path. Native timings include index, 100 date lookups, meal/event/close, preview/issue where eligible, progress projection and CheckIn stages; see machine-readable evidence. Backup validation is measured in-browser separately from the automation cost of transferring archive bytes.

Cold startup and backup/restore must validate the complete archive and remain linear. Historical calibration cross-checks can still be quadratic in the number of issued hypotheses on a cold decode; the representative curve varies day/fact history, not arbitrarily many hypotheses. This cold correctness path was retained rather than replace calibration semantics. Full snapshot format capacity is a known engineering limitation; quota is a typed recoverable result, not silent loss.

## Regression and dependency audit

Full CI on `3be42a345ffee496575f111fec95e8264177f5e9`: Release build **0 warnings / 0 errors**; **683 .NET**, **93 JS**, **34 Python**, no skipped .NET tests. The complete browser step and published PWA step pass, including storage failures, 16 synthetic states/accessibility, mixed backup, history budgets and two-version PWA transition. The matrix runs 26 distinct browser scripts, with extra anatomical and published-offline GeometryWarp modes. [Machine-readable verification and script list](evidence/pre-ui/ci-verification.json). Local targeted checks independently pass; the stale local development-server asset response found during an extra run disappeared after a clean rebuild/restart and the unchanged CheckIn smoke then passed fully.

Retain all suites: avatar, creation, skeletal, strength, history, forecast, composition, muscle forecast, product, ActivityDay, Nutrition, Hypothesis, CheckIn, GeometryWarp/WebGL, anatomical research, errors/recovery, backup/restore and published offline PWA. Added history ratios, developer-loader isolation, local diagnostic privacy, typed errors/caches, storage health, quota and PWA version transition. Layout screenshots remain distinct from behavioral contracts.

[Dependency/security/license audit](PRE_UI_DEPENDENCY_AUDIT.md) documents NuGet advisory results, pinned local libraries/assets, notices, restricted-file guard and source disclosure scan. No new package or heavyweight dependency. License notices are preserved; vendor bytes are protected against Windows line-ending conversion.

## Next UI/user-test phase

Safe changes and immutable semantics are listed in PRE_UI_CONTRACT. The next phase may redesign presentation against service/read-model APIs and reproducible fixtures, with behavioral regression protecting facts and transitions.

Non-blocking external/empirical gaps: physical low-end-phone performance, real-photo quality, real-user predictive accuracy, formal privacy/legal review and named pilot owner, DeltaShape/Pseudo-DXA data/prerequisite/rights access. AI renderer remains deferred. No participant or application was contacted/submitted by this task.

The engineering gates for a separate UI redesign pass. No unresolved engineering blocker is known in the tested scope. Whole-envelope capacity/linear costs, the unmet 2x target and cold many-hypothesis calibration limits are explicit constraints, not claims of constant-time access. Review and merge of PR #31 remain human decisions; this is not authorization to recruit participants or certify accuracy/privacy compliance.

READY_FOR_UI_REDESIGN = YES
