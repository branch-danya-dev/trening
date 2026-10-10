# Model provenance, fallback and release reproduction

Release policy `forecast-model-registry-1`, 2026-10-09. This package hardens the existing local providers; it does not establish human predictive accuracy. See [current status](MODEL_VALIDATION_STATUS.md).

## Immutable provenance

`ModelRegistry.FreezeV1` is the central immutable definition of composition, procedural shape, muscle geometry, photo reconstruction policy, calibration, heuristic/evidence ranges, endpoint and local warp versions. It pins SHA-256 of MakeHuman, muscle atlas, optional anatomical sidecar and the existing local pose model. A deterministic digest covers the complete definition; attribution references accompany it. `ShapeModelHash=null` means no trained DeltaShape artifact exists, not a missing downloaded model.

New `ForecastSnapshot` objects freeze this manifest. New `Hypothesis.ExactEndpoint.Geometry` copies it into the descriptor covered by the endpoint and core hashes. Avatar builder/fitter/corrections, photo source analysis provenance, calibration revision and exact geometry hash remain separately frozen in their existing records. The registry names the release capability; it does not claim every origin used a photograph or that a derived value is factual.

Legacy missing manifests remain null and are omitted during serialization. No migration fills them with today's versions. Their saved points, descriptor hashes and meshes remain unchanged. New and legacy geometry can be identical even though a new descriptor includes additional provenance and therefore a different hash. Unknown/tampered manifests fail validation. To upgrade, add an immutable new registry definition and explicit replay support; do not edit v1 or relabel old snapshots.

The registry's production gates are read-only: procedural shape/muscle; anatomy default false; DeltaShape false; Pseudo-DXA false. There is no configuration file, query parameter or research artifact that turns these into production defaults. Explicit anatomical descriptors remain available to offline research and historical replay. Research selection must freeze the matching registry; changing only the provider is rejected. No new shape-provider interface is shipped before an independent GO.

Minimal diagnostics in the existing hypothesis details show the active frozen shape/muscle/range versions and blocked research status. No new user flow or UI redesign is introduced. Opt-in validation exports include allowlisted public model versions/digest, without raw scans, private IDs or source-photo hashes.

## Runtime fallback matrix

| Condition | Enforced behaviour | Historical/factual boundary |
|---|---|---|
| Invalid composition/input | `ForecastSnapshot.Create/Validate`, preview and issue/store validation reject; no valid candidate to save | Never fabricate an endpoint or change a BodySnapshot |
| DeltaShape absent/OOD | No production DeltaShape path is registered; existing procedural provider is selected. Research `Support.assess` returns fallback/widening reasons | An unvalidated latent result cannot replace an issued endpoint |
| Anatomy NO-GO, missing/corrupt asset in a new research build | Production default stays procedural. Explicit research builds use existing warned procedural fallback and authoritative girth reconciliation | A **frozen** anatomical endpoint with unavailable pinned asset refuses exact reconstruction; it is not silently replayed with a different provider |
| Photo quality insufficient / reconstruction rejected | Existing `CheckInQualityPolicy` and service preserve last-known-good Avatar; supported independent manual facts can still be saved | Photo-only gaps do not invent weight/BF; revisions/origin/cycle remain explicit |
| GeometryWarp unsupported or quality gate fails | Existing local render policy reports failure/3D availability; no remote retry | Generated/warped imagery never becomes an observation or a calibration label |
| Unknown/corrupt registry or endpoint hash | Reject exact build/issue/import path requiring that descriptor | Original stored bytes are not rewritten with current defaults |

Existing structural tests cover asset substitution, corruption, safety limits, missing sidecar, photo/manual conflicts, poor photo retention, exact endpoints, old/new provider replay, interrupted writes and backup/restore. Registry tests additionally bind bundled hashes and reject mixed provider/manifest metadata. Full browser regression covers GeometryWarp legacy/anatomical descriptors and offline PWA.

## Calibration audit

No new empirical model has been fitted. Existing local residual adaptation remains bounded and versioned:

- `ForecastCalibrationService` partitions by composition version, enforces issue/through-date availability and groups by `(FactId, Metric)` before weekly fitting. A fact cannot multiply evidence merely because multiple forecasts were saved.
- `HypothesisService.Calibration` additionally requires the same tracking cycle, an evaluated eligible outcome and `RecordedAt <= cutoff`. Late, missing, cancelled and manual-reset outcomes are not physiological training evidence. Manual reset archives the prior cycle.
- Source quality, outlier and minimum duration/count rules remain active. Photo-derived measurements are downweighted under the existing method policy; measured and fitted quantities stay distinguishable. Muscle regional response calibration remains separate from ordinary girth factors.
- Historical forecast points/ranges never re-run the engine after calibration. Only a future origin receives a new calibration revision. Missing legacy composition metadata maps to the legacy partition, not the current provider.
- DeltaShape is absent; therefore no speculative shape-calibration store is created. Any future GO integration must partition by shape/model hash before admitting evidence. The offline validation analyser never writes calibration to the app.

## Uncertainty audit

`expected-range-1` is a heuristic expected range. `evidence-range-1` widens it for preliminary maturity, gaps and partial/unknown nutrition/activity. Local historical residual MAE may widen the existing per-person heuristic, but this is not independently validated population calibration. Research `train-support-heuristic-1` uses input support/PCA distance/residual checks, reports reasons and a widening/fallback decision; its score is not a probability.

There is no empirically calibrated human error distribution from this package, no accuracy percentage, and no statistically justified confidence interval claim. Existing technical pose confidence scores and quality categories must not be described as body-shape accuracy. Removed wording that treated the old 2–3 cm photo error / 0.5 cm repeatability targets as established application performance; future tests must measure these.

## One-command reproduction

Prerequisites: .NET 10 SDK, Python 3.12 with `tools/requirements-shape-research.txt`, Node and Playwright with an installed Chromium. Use the project's CI-pinned Playwright version. No scientific data download or paid service occurs. Dependencies are installed explicitly by the operator/CI, not fetched by the runner. `NODE_PATH` can point to an existing Playwright installation; `BROWSER_CHANNEL=msedge` is supported for a local Windows desktop audit.

```text
python tools/reproduce_model_validation.py work/model-validation
```

Use a **fresh** output directory. The command records source commit/dirty state, per-command logs, exit codes/times and JSON SHA-256 inventory; it fails on any required check. It builds Release, runs the tracked research path guard, reproduces legacy/current Hall checks, the bundled anatomical benchmark, GeometryWarp fixtures and browser benchmarks, current Avatar/issue/CheckIn browser flows, domain timings and an empty prospective report. `--core-only` explicitly labels browser omission and is not a full release gate. Full .NET/JS/Python and published offline regression remain in CI.

Private real-data evaluation is optional and requires both `--deltashape-config <private-config>` and `--deltashape-output <approved-private-output>`. The CLI applies the data-access gate before mesh loading; no synthetic override exists. Private output/logs must be outside Git and the structural evidence directory. All restricted results need custodian export review. This command does not create a trained candidate or grant GO. Pseudo-DXA remains blocked; no ablation is pretended.

Performance JSON separates first process/adapter calls from warm samples, and records assets, allocations and heap/working-set snapshots. Browser startup measures a fresh context and reload, asset transfers and JS heap. Domain issue/CheckIn timing excludes persistence; browser reports cover actual UI/storage. Neither first call nor fresh browser means OS disk caches were flushed. These are headless **desktop** measurements; total/peak browser memory, mobile thermals and physical low-end phones remain later test work.

## Privacy and release audit

Restricted raw scans, registered research shapes, actual participant manifests and learned weights stay in an approved external root, excluded from Git. The registration/PCA/evaluation CLI enforces new output files inside the approved data root and refuses overwriting frozen artifacts. Registration source files must occur in the approved pair inventory. Real split manifests stay within the external pair-manifest directory. Support/fallback reports retain observed and missing denominators; missing OOD metadata is never counted as supported evidence. The tracked-path guard and ignore rules are additional safeguards, not a content DLP guarantee. No restricted dataset was obtained in this package. Synthetic test meshes are labelled fixtures and cannot populate an accuracy report.

Numeric exports are opt-in, use within-package aliases and omit photos by default. Exact dates and body measurements are still potentially identifying; intake requires a coordinator's random study ID and consent confirmation. Full backup is intentionally private and is not a validation export. Per-person packages, identity linkage and private agreements are not stored in repository evidence.

The research cache tool previews deletions by default and only removes recognised content-hashed files under its marked root; it rejects symlinks/junctions/unrecognised files. It is not secure erasure of SSDs, cloud copies or institutional backups. Retention/withdrawal covers those separately. BodyParts3D/MakeHuman and the pre-existing photo stack attribution remain packaged. Pseudo-DXA code/weights and restricted data are not vendored. No render becomes a fact.
