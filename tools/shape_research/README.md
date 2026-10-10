# Offline shape research (C0)

This is infrastructure, **not a validated model**. Python 3.12 + `numpy==2.3.5`; .NET 10 for the production baseline adapter. No remote service is used. Keep all real inputs and outputs in an approved external restricted root. Real-data commands require the private gate; current public example denies access.

From repository root:

```sh
python -m pip install -r tools/requirements-shape-research.txt
python -m unittest discover -s tools -p 'test_*.py' -v
dotnet test tests/WorkoutCalculator.Tests -c Release --filter FullyQualifiedName~ShapeResearchBridgeTests
```

These tests include explicitly synthetic geometry, composition and consent fixtures. Their passing status says nothing about human accuracy. No dataset or model downloads occur.

## Data contract and commands

Set `PYTHONPATH=tools` (PowerShell: `$env:PYTHONPATH='tools'`). `python -m shape_research --help` lists commands. `LongitudinalBodyPair` in `contracts.py` is the provider-independent typed contract. JSON keys follow its snake_case field names. All dates use ISO format and can be consistently shifted by the custodian. Composition is kg, geometry metres, optional clinical DXA lean soft tissue must be converted to documented FFM including bone before use.

```sh
python -m shape_research split /restricted/pairs.json /restricted/split.json --seed preregistered-seed
python -m shape_research register /restricted/registration-config.json /restricted/registered.json
python -m shape_research pca-report /restricted/pca-config.json /restricted/curves.json
python -m shape_research evaluate /restricted/evaluation-config.json /restricted/results.json
```

Registration/PCA/evaluation configs require `data_root`, `repo_root`, `pairs`, `split`, `gate`. The private gate follows `docs/data-access/data-access-gate.example.json` with actual approval references and authorised releases. The registered sources and model artifacts remain restricted. The gate checks file inventory, permission attestations, retention and split. An operator must retain the signed supporting evidence; booleans are not legal proof.

`repo_root` must identify the running checkout. Registration sources must occur in the approved pair inventory. Registration/PCA/evaluation outputs must be new files inside the approved data root; existing artifacts are not overwritten. Real split manifests must stay in the external pair-file directory. Missing support metadata has its own denominator and is not counted as in-support evidence. The full structural reproduction command is documented in [MODEL_PRODUCTION_HARDENING](../../docs/MODEL_PRODUCTION_HARDENING.md).

Registration adds `source`, `template`, `mapping` paths relative to `data_root`; `unit`, a proper 3×3 `orientation` matrix, `source_landmarks`/`target_landmarks` arrays, optional `height_scale` and `max_rms_m`. OBJ accepts triangle faces only; JSON uses `vertices: Nx3` and `triangles: Mx3`. No materials/external OBJ references are loaded. A versioned `barycentric-map-1` contains both topology hashes, one source triangle index and three nonnegative unit-sum weights per target vertex, with SHA-256 over canonical JSON excluding `sha256`. A provider's reviewed correspondence is required; C0 does not invent an arbitrary non-rigid scan fit. Identity mappings are valid only for matching topology.

PCA config adds `registered_shapes` (relative JSON rows with `participant`, `sex`, flattened `shape`), `dimensions` and `topology_hash`. The file must contain TRAIN/VAL only. Shared and sex-specific reconstruction curves are reported; inadequate rank/support stays explicit. The `PCA` API serializes immutable review artifacts with training IDs and split hash. Treat these as restricted, not public assets. Test data cannot tune a dimension.

Evaluation config adds private `freeze`, `model`, preregistered `policy`, and `cases` mapping pair hashes to relative prediction files. `freeze_evaluation` binds inventory, model, split and thresholds. Every held-out nonexcluded pair is required. Each case contains `actual` and `candidate` meshes on common research topology, `model_hash`, a `procedural` bridge result, an explicit `makehuman_to_research` correspondence, optional ordered girth `rings` and closed region face definitions. Open regions have unavailable volume. Register/review prediction artifacts before evaluation; don't build correspondence from outcome errors. Generic API supports matched auxiliary ablations; no Pseudo-DXA weights are loaded.

Required policy: `information_policy` (`t0-only` or `observed-composition-oracle`), `surface_improvement_fraction`, `girth_improvement_fraction`, `maximum_subgroup_regression_fraction`, `minimum_test_participants`. Define thresholds before TEST based on intended use and VAL; the code does not pretend universal thresholds exist. Reports never auto-activate a production model; independent GO review also requires rights, subgroup/fallback and physical output constraints.

## Exact production baseline bridge

```sh
dotnet run -c Release --project tools/WorkoutCalculator.ShapeResearch -- /repo /restricted/request.json /restricted/procedural.json
```

Request fields: `profile` (application BodyProfile, including explicitly supplied T0 girths), `t0`, `horizonDays` (1–364), `informationPolicy`, `forecastInput` for T0-only mode, or `deltaFatKg`/`deltaLeanKg` for oracle mode; `synthetic` marks test cases. T0-only invokes `ForecastSnapshot`/`BodyShapeForecast` and exact-day interpolation, then MakeHuman fitting with frozen procedural muscle fields. Oracle mode uses the application's composition-to-girth mapping and explicitly labels itself nonprospective. Missing T0 girths must be provided through a documented baseline estimation policy, never copied from T1. Input and preprocessing eligibility is a caller/gate responsibility; this adapter has no dataset access or product writes.

The output includes model/asset versions, meshes, fit residuals and timing. It stays in MakeHuman coordinates/topology until the explicit evaluation mapping. Long Fenland horizons beyond the current engine range are rejected, not extrapolated.

## Prospective report, empty until testing is authorised

```sh
python tools/validation_analysis.py docs/data-access/prospective-intake.example.json /private/validation-report
```

Produces offline `report.json`/`report.html` with `NO_PROSPECTIVE_DATA`, not synthetic accuracy. Later replace the empty private intake with `participants` entries: `study_id` (random ≥8-character pseudonym), `path`, SHA-256 `sha256`, `consent_confirmed: true`. One latest package per participant. ZIP reader reads only bounded `analysis.json`; no photos are extracted. Old exports without verified origin are excluded. Exact targets form the primary report; early/late outcomes are counted separately for later sensitivity analysis. Suppress cells with <5 people; repeat observations receive participant-level aggregation and same-day metric deduplication. No calibration is fitted by this tool.

## Cache removal

Create a dedicated directory with `.shape-research-cache` only after approval. Cache entries are content-hashed JSON; other files, subdirectories and symlinks cause refusal.

```sh
python -m shape_research delete-cache /restricted/dedicated-cache
python -m shape_research delete-cache /restricted/dedicated-cache --execute
```

First command previews. The second removes only enumerated hashed cache entries. It is not raw-data deletion or a promise of secure SSD/backup erasure.
