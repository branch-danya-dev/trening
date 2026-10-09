# Readiness for the next user-testing/UI phase

Readiness `non-ai-forecast-readiness-1`, 2026-10-09. The package prepares controlled tests; it does not launch them. Current layer decisions are in [MODEL_VALIDATION_STATUS](MODEL_VALIDATION_STATUS.md).

## Is the core ready?

The core is suitable for a **controlled feasibility/usability pilot after final PR review**, with current procedural providers, explicit estimates/ranges and the existing locked-origin lifecycle. Before recruiting, the study owner must name the responsible contact, approve consent/data handling and define the sample/analysis plan. No study owner/ethics approval or real participants are invented here. This is not readiness for an accuracy marketing claim or unrestricted clinical use.

C0 and the prospective export/analyser are available despite the data-access blocker. DeltaShape/Pseudo-DXA remain blocked and off; anatomical muscle remains research-only. The UI is unchanged except for minimal version/status diagnostics in existing hypothesis details. Renderer/GPU/provider integration remains deferred.

## Safe statements and unvalidated metrics

Safe functional statements: the app stores explicitly confirmed observations; separates facts, derived Avatar values and forecasts; freezes hypothesis inputs/version/endpoint; compares later recorded outcomes; builds local 3D and eligible local GeometryWarp; keeps normal operation local/offline with available assets; exposes coverage and heuristic ranges. A registered model version or structural benchmark is provenance, not a probability of success.

Still unvalidated: prospective weight/BF/composition error; girth error and range coverage by horizon/subgroup; real change in global/regional shape or muscle mass; photo repeatability and absolute accuracy; avatar likeness; real-photo warp quality; adherence and nutrition/activity reporting effects; mobile memory, latency, thermal behaviour and recoverability on physical low-end phones. Fitting a mesh to an entered girth cannot independently validate that girth.

## What the first tests must collect

Use [prospective-fitness-1](REAL_USER_VALIDATION_PROTOCOL.md). Collect baseline scale/tape measures with method/quality, explicitly optional BF/photos, reviewed Avatar revision/quality, frozen hypothesis origin and exact 14/30-day endpoint, daily activity/nutrition completeness, actual outcome date/source/quality, independent target-day measurements, missingness, reset/withdrawal reasons and device/browser/build. Optional 28/56/84-day research checkpoints remain separate. Keep photos local by default; request numeric exports by opt-in only.

A coordinator assigns random study IDs and verifies consent, package hashes and one latest package per participant. Analyse errors within participant before aggregation, stratify by actual horizon and baseline sex/age/BMI/source/model, suppress small cells and report exclusions/denominators. Collect failures and dropouts as well as successful cycles. The empty report remains `NO_PROSPECTIVE_DATA`; synthetic fixtures never fill it.

## UI flows to observe before redesign

Observe onboarding and fact/estimate comprehension; photo framing/retakes; Avatar review and corrections; starting/resetting a tracking cycle; distinguishing rest, missing and complete days; nutrition serving/completeness inputs; understanding assumptions and heuristic ranges; saving/finding a hypothesis; completing the target-day CheckIn; reading Forecast-vs-Fact and current-versus-origin revisions; local warp failure/3D fallback; backup/restore, offline recovery, privacy/export and deletion. Record completion times, errors, hesitation, abandonment and participants' own explanations. Viewport regression is only layout evidence.

## Verification record

The final implementation must retain green Release (zero warnings/errors), .NET/JS/Python regression, browser lifecycle, historical replay/GeometryWarp and published PWA checks. The final PR and its exact CI run are the authoritative code gate. A fresh reproduction directory records commit/dirty state, complete/partial status, per-tool logs and hashes. Desktop performance and final checks are appended below after execution; no physical-phone result is inferred.

## Remaining human actions

Review the final hardening PR. Separately decide the pilot/UI-review scope and responsible data handling. If pursuing research data, complete the marked applicant fields and obtain eligibility/secondary-use/derived-weight/security decisions from each custodian before submission or training. The user has prohibited current application submission; the prepared dossiers are ready for that later step.
