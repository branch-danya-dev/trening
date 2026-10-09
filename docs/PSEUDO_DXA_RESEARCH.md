# Pseudo-DXA research decision

Audit 2026-10-09: **BLOCKED_PREREQUISITE_AND_RIGHTS**, production disabled. No weights downloaded, no inference, no provider/API/GPU integration.

The [official repository](https://github.com/LambertLeong/Pseudo-DXA) was inspected at commit `196630285aaa3637b4990e0b5ff2d445c10c3544`. Its [LICENSE](https://github.com/LambertLeong/Pseudo-DXA/blob/196630285aaa3637b4990e0b5ff2d445c10c3544/LICENSE) is GPL v3. The README makes weights available by request; its mesh preparation uses Meshcapade-fitted topology, and conversion for proprietary DXA analysis is not public. Source licensing does not establish rights to the weights, training data or that preprocessing. The model uses Shape Up! data, so participant overlap with a future DeltaShape test set is a material leakage concern. Ask the authors for separate weight/data/commercial and redistribution terms and overlap provenance.

This code license is not characterised as a non-commercial ban. Distribution and integration require a concrete license review. No code or weights from the repository are vendored here.

## Frozen evaluation design, pending an independent DeltaShape benchmark

Run three separate hypotheses under the same participant manifest and fixed model:

1. A: T0 external shape → regional composition prior available at origin.
2. B: predicted T1 shape → auxiliary consistency diagnostic; never a factual DXA result.
3. C: approved T0 prior as additional DeltaShape input.

Require matched `DeltaShape without` / `DeltaShape with` predictions on identical held-out participants, identical T0 inputs and horizon. Fit any auxiliary transformations on TRAIN, tune on VAL, freeze before TEST. Never use true T1 shape-derived estimates as an input to the T1 predictor. Investigate upstream model pretraining overlap; unknown overlap prevents an independent GO conclusion.

Compare global surface, key girth and regional errors plus constraints, support/fallback rates and subgroup regressions. Preregister material improvement and acceptable regression thresholds before opening TEST. Reject integration if both required metric families do not improve, sample support is insufficient, licensing is unresolved or the OOD/fallback path is unsafe. Publish negative results. The generic paired-comparison harness can compare approved prediction tables; it does not grant access or execute Pseudo-DXA.

Pseudo-DXA output is always an inferred research estimate. It never becomes `BodySnapshot`, measured muscle mass, factual DXA, a medical result, or a source for empirical product calibration.
