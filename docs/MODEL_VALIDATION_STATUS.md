# Model validation status

Status document `model-validation-status-1`, 2026-10-09. A structural GO means the current engineering gates pass; it does **not** mean independently demonstrated predictive accuracy on people. This matrix is the current decision; older roadmap milestones are historical context.

| Layer | Status | Default? | Evidence | Blocking issue / remaining limit |
|---|---|---|---|---|
| Composition v3 (`hall-forbes-2+residual-1`) | GO — structural/reference benchmark | Yes | Hall/Forbes reference comparison, mass/water/macronutrient invariants, version-partitioned calibration and regression | Prospective weight/composition error and range coverage on intended users unvalidated; sodium/ECF remains disabled |
| Current Avatar reconstruction | GO for controlled testing; empirical accuracy unvalidated | Yes, reviewed and locked revision | Deterministic MakeHuman fit, factual/derived separation, correction/check-in gates and browser lifecycle | Independent photo/tape/shape accuracy, repeatability, likeness and subgroup failures |
| Procedural muscle geometry | GO — structural | Yes | Protected regions, girth reconciliation, bounded fields and frozen replay | Visual/local adaptation accuracy on real people unvalidated |
| BodyParts3D anatomical | NO-GO for default; research capability | No | Pinned source/license/sidecar, 13 groups, procedural comparison and visual audit in #28 | Visual advantage not established; incomplete posterior coverage; constraints can remove the layer |
| DeltaShape | BLOCKED_PENDING_DATA_ACCESS | No | C0 contracts, explicit correspondence, TRAIN-only PCA, leakage/support/metric harness and actual procedural baseline bridge | Real paired longitudinal data, applicant/approval/security/derived rights, validated registration and independent held-out benchmark |
| Pseudo-DXA | BLOCKED_PREREQUISITE_AND_RIGHTS | No | Official source/license audit, A/B/C and paired ablation design | DeltaShape prototype/benchmark absent; weights/preprocessing/data/commercial rights and upstream overlap unresolved |
| GeometryWarp | GO — structural local transform | Yes, eligible local requests | Frozen geometry, source/target depth and bounded repair, old/new provider replay, WebGL/offline regression | Real-photo identity/scene/texture quality and perception validation |
| Photorealistic renderer | DEFERRED | No | None added | Intentionally outside this package and postponed to the end of the project |

## Evidence and reproducibility

PR #28 was merged with a normal merge commit `c106bdff501558f6f9f3e282d36f91588310a556`; [main CI passed](https://github.com/branch-danya-dev/trening/actions/runs/37978604313). C0 [PR #29](https://github.com/branch-danya-dev/trening/pull/29) passed [full PR CI](https://github.com/branch-danya-dev/trening/actions/runs/37982797039), then merged normally as `ec8d061a881c690786109ca723b200dd728335bf`; [new main CI also passed](https://github.com/branch-danya-dev/trening/actions/runs/37985461073). Final hardening starts from this green base and adds immutable registry/frozen metadata, provider diagnostics, audited fallback/calibration/uncertainty boundaries and [one-command reproduction](MODEL_PRODUCTION_HARDENING.md). Exact final checks and measured desktop results are recorded in [readiness](USER_TEST_READINESS.md) and the final PR/CI artifacts.

All new numerical fixtures are synthetic **structural tests only**. No real C1 fit/test, Pseudo-DXA inference, empirical population calibration, external user test or application submission occurred. There is no trained DeltaShape artifact or hidden research default. Procedural physiology/mass allocation remains authoritative.

## Access decision

The [six-question access matrix and exact blockers](data-access/ACCESS_STATUS.md) and reusable [Shape Up!](data-access/SHAPE_UP_REQUEST.md) / [Fenland](data-access/FENLAND_REQUEST.md) dossiers are prepared with unfilled applicant fields. The independent project has no institution, responsible research investigator/email, ethics approval or partner. Storage country is deliberately undecided. The user expressly prohibited submission on their behalf.

The linked Shape Up! request form was closed when inspected. Fenland's ordinary route requires a bona fide research organisation; its commercial route is case-by-case, and independent longitudinal optical surfaces are not confirmed. These are access/data-definition blockers, not failed predictive experiments. Paper results, public code licenses and downloaded forms do not confer model training/commercial deployment rights.

## Next decision

Proceed to a separately authorised controlled usability/prospective pilot after final code review and concrete consent/governance setup, using current procedural providers. Research access can proceed later when an eligible applicant and permissions exist. No additional internal model or renderer is a prerequisite for observing the current product. Follow [prepared protocol](REAL_USER_VALIDATION_PROTOCOL.md); do not state human accuracy until the resulting independent evidence supports it.
