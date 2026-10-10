# DeltaShape C0 — research contract and gates

Status: `BLOCKED_PENDING_DATA_ACCESS` for real training/evaluation. The C0 tools are testable infrastructure, not a trained or validated predictor. No model is used by the application. Procedural geometry remains default.

Executable entry points, configuration fields and reproduction commands: [research tool README](../tools/shape_research/README.md). The .NET bridge uses the actual application prediction, not a duplicated formula. The baseline for this implementation is merge `c106bdff501558f6f9f3e282d36f91588310a556` after [full green main CI](https://github.com/branch-danya-dev/trening/actions/runs/37978604313).

## Information boundary

`LongitudinalBodyPair` records project pseudonym, visits/dates and exact horizon, sex/age/height, T0/T1 measured composition with compartment definitions, optional regional composition and intervention labels, source/version, mesh references/hashes and exclusion flags. Do not store raw research records in a product store. Never infer FFM from DXA lean soft tissue without BMC/compartment agreement.

Two experiments must remain separate:

- **Conditional shape**: use observed Δfat/ΔFFM to compare global shape mapping under the same composition budget. This isolates geometry, but is an oracle-composition experiment, not prospective prediction accuracy.
- **End-to-end**: composition deltas must be forecast using only T0-available inputs. T1 outcomes cannot choose intake, program, calibration or model parameters. Compare at the actual elapsed day through the production interpolation path.

## Registration contract

Import bounded triangulated OBJ or explicit JSON meshes, with unit and orientation metadata. Canonical coordinates use metres, +Y up and +Z front. Estimate a proper rigid landmark transform (no reflection) from nondegenerate correspondences. Height normalisation is opt-in, recorded and reversed before dimensional evaluation; never normalise T0 and T1 independently in a way that removes true change.

A source-to-research barycentric correspondence artifact is mandatory and bound to both topology hashes. It may come from an approved registration process or an already registered provider release. Identity mapping is valid only for identical canonical topology. Landmark alignment alone is **not** non-rigid correspondence. C0 does not claim to solve arbitrary naked scan registration without data; failed/missing correspondence is rejected, never guessed by vertex index or silently treated as MakeHuman.

The MakeHuman bridge emits its own positions/topology and exact production model metadata. Evaluation requires a separate explicit MakeHuman→research map. Research→output mapping is not production integration and cannot activate a candidate provider. Record residuals, topology, orientation, source/model/config hashes, height transform and quality thresholds in every registration artifact; reject degenerate/nonfinite/unsafe surfaces. Numerical thresholds are engineering rejection policies, not established scanner accuracy.

## Representation and model

First baseline is deterministic PCA/SVD on TRAIN registered shapes. Persist mean, components, eigenvalues, topology hash, training participant manifest, transform/version and artifact hash. Sign-normalise components and record runtime/numerical library versions. Bitwise reproducibility is scoped to the pinned environment; test numerical equivalence across platforms. Report explained variance and TRAIN/VAL reconstruction error versus dimension. Choose dimension on VAL only; optional shared/sex-specific comparison requires supported strata. No TEST-driven selection.

`IDeltaShapeModel` accepts start latent, demographics, baseline composition, Δcomposition, actual horizon and optional regional/training descriptors. A zero-change baseline returns start latent and identifies itself as non-production/non-predictive. Future regression belongs in latent space. Record unavailable features instead of imputing them from T1.

OOD diagnostics use TRAIN support: whitened PCA score distance, reconstruction residual, input ranges, horizon, composition delta ranges and categorical/subgroup support. A distance is not a probability. Unsupported inputs yield an explicit procedural fallback decision; do not turn the score into an accuracy percentage.

## Evaluation and anti-leakage

All visits/pairs of one participant stay in one deterministic split; validate complete manifest membership and mutually exclusive assignments. The same person across approved linked studies must use the same project pseudonym. Train-derived preprocessing/model artifacts must record only TRAIN contributors. Freeze configuration/model/split hashes before TEST. Unknown upstream model overlap blocks independent GO.

Metrics: correspondence vertex mean/median/p95; symmetric surface distance with method/sampling declared; front/side/back orthographic silhouette IoU and contour distance with shared camera/bounds; ring girth residuals; closed regional and total signed-volume errors; composition consistency; support/fallback counts. Missing rings, incomplete/open regions and absent labels return unavailable with denominators, not zeros. Front/back orthographic silhouettes can coincide; they are not independent evidence.

Report ≤42, 43–84 and >84 days, plus exact 28/56/84-day bins only when present. Report sex, baseline BMI bands, age bands, fat-loss/gain and strength-label subgroups. Aggregate within participant before population summaries. Small cells are suppressed in public reports; restricted reports retain counts for audit.

## Before C1

Require obtained real data, documented permitted analysis, understood derivative-model rights, approved secure root outside Git, pseudonymised IDs, inventory hashes and a prepared participant split. The gate is an accountable operator attestation backed by private agreements, not an automated legal decision. No data/permission means no real PCA fitting, no TEST evaluation and no model GO.

Pre-register material improvement thresholds on both surface and key girth/regional families, catastrophic subgroup tolerances, required supported sample sizes and constraint tolerances before TEST. GO also requires height/volume/girth/composition constraints and tested OOD/fallback. Report NO-GO honestly. A boolean entered into a JSON file or passing synthetic tests is never independent evidence.

## Later integration, deliberately absent

Only after independent GO: versioned provider, compact approved local artifact, composition→global shape→muscle→reconciliation pipeline, frozen model/hash/OOD/fallback endpoint metadata and exact replay. Do not alter physiology, historical hypotheses or factual snapshots. No managed renderer, GPU or provider API is introduced by C0.

## Production boundary after C0

The immutable model registry identifies the existing procedural bridge. DeltaShape is disabled, has no production weights and cannot be enabled by a research artifact. Reproduce available structural layers with `python tools/reproduce_model_validation.py work/model-validation`; see [hardening](MODEL_PRODUCTION_HARDENING.md). This does not run C1 without a private approved data gate.
