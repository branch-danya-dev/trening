# Phase 6 verification — Current Avatar check-ins

Date: 2026-10-09. Baseline #25 was merged by ordinary merge commit `106efdaf28cdb7fdebb91b8b8bf2c98dd254f2a7`. [Full main CI](https://github.com/branch-danya-dev/trening/actions/runs/37899920321) passed before creating `codex/avatar-checkins`.

## Results

- Release solution build: **0 warnings, 0 errors**.
- .NET: **607 passed**, including 31 CheckIn cases. JavaScript: **72 passed**, including 11 transaction/export/backup cases.
- Desktop Edge browser matrix: **15 suites passed** — skeletal, strength, history, forecast, composition, muscle-forecast, product, avatar, avatar-creation, activity-day, nutrition, hypothesis, checkin, errors, offline.
- New [browser report](report.json): manual partial facts, good pair with PhotoDerived-only snapshot, bad/conflicting photos preserving active revision, back-only envelope, same cycle/origin, target-date check-in, reload before explicit outcome attachment, immutable endpoint, exact clean restore and private opt-in export. No browser errors.
- [320 px](checkin-320.png), [390 px](checkin-390.png), [1400 px](checkin-1400.png): no document or panel overflow; inspected stacked inputs and wrapped action labels. [Forecast vs Fact](outcome.png) contains only known factual weight in the target scenario.
- Published `/trening/` PWA, network disabled before observation: manual and PhotoDerived check-in, new revisions, local imagery and backup passed. The accepted photo fixture supplies deterministic pre-analyzed silhouettes; it does not certify first-use ML initialization offline.

## Integrity evidence

Domain coverage includes weight-only/girth-only facts, no empty snapshot relaxation, supported photo estimates, source rejection, residual/conflict gates, prior correction protection, deterministic geometry, same-cycle factual updates, recalibration cycle changes, frozen forecasts, known-field outcome weighting, exact store reload and cross-reference validation. Both same-session ingestion and final commit retry are idempotent. An older prepared job loses CAS and survives as an observation without replacing newer geometry.

JavaScript fault injection interrupts after journal preparation, CheckIn write, BodySnapshot write and Avatar write. Shared reads/writers are blocked while the journal exists; recovery restores every after-image once. Tests also cover corrupt journals, quota before staging, forbidden target keys and restore-generation protection even when restored bytes are identical. Domain and browser tests stop after factual commit before outcome linking, reload, then attach the same fact idempotently. No hypothesis attachment occurs before its fact exists.

## Desktop performance sample

Single observed run, not percentile/SLA or low-end-device validation; browser regression work shared the desktop. Raw measurements are in [report.json](report.json).

| Operation | Observed time |
|---|---:|
| Open CheckIn, including increasing history validation | 66–1241 ms |
| First manual partial observation, total | 1008 ms |
| Good cached-photo observation, total | 1220 ms |
| Cached photo quality/measurement extraction | 77–108 ms |
| Reconstruction preparation (including strict references) | 128–681 ms |
| Transaction commit, including persisted graph validation | 79–770 ms |
| Target-date observation with accumulated hypothesis graph, total | 6000 ms |
| Existing hypothesis evaluation after reload | 1492 ms |
| Clean restore through ready model | 3541 ms |

[Cold/warm photo engine probe](photo-performance.json): 3882 ms cold and 15 ms warm for an 800×1000 blank image rejected as “no person.” This measures engine initialization/detection rejection only, **not successful human-photo analysis latency**. The synthetic image is explicitly marked Synthetic, never submitted to CheckIn, and creates no factual observation. Successful-photo end-to-end timing remains dependent on a real input pair/device; accepted reconstruction timings above use deterministic silhouettes. No runtime/fitter optimization or unrelated expensive build was introduced.

## Reproduce

Run the existing Release build/test commands and CI workflow. With the Release app on port 5256, `tests/browser/checkin-smoke.cjs` accepts `APP_URL`, `CHECKIN_OUTPUT`, `NODE_PATH` and optional `BROWSER_CHANNEL`. The optional `checkin-photo-performance.cjs` accepts `PHOTO_PERF_OUTPUT` and records its bounded raw-engine probe. The workflow runs the full matrix and uploads CheckIn evidence. The published offline test uses port 5257 and `/trening/`.

## GO boundaries

All local implementation gates pass; final release assessment also requires the full CI on the PR's current head. PR is review-only and must remain unmerged with auto-merge disabled. Physical low-end-phone validation and empirical avatar/photo accuracy remain open. Source attestation is not forensic synthetic-image detection. Conservative quality rejection retains valid manual facts. Photo-only or girth-only facts remain valid observations but existing Phase 5 evaluation still requires factual weight. Phase 7/8, backend/photo upload and physiology changes are excluded.
