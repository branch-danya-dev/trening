# Forecast v3 composition benchmark (generated)

Reference `hall-2011-appendix-rk4-1`; baseline main `471449780756f091234849d3055bd1e149f9312e`.
4096 scenarios; LCG seed 20261009; scenario SHA-256 `69ACB731B555A862EE2F2EFC1BD201FABF700372A769E8C376E8E234BBEA56C8`.

Reproduce: `dotnet run -c Release --project tools/WorkoutCalculator.CompositionBenchmark -- --legacy-only --check`. Use `--write` to regenerate. No network is used. See tools/WorkoutCalculator.CompositionBenchmark/REFERENCE.md for scope, equations and assumptions.

Errors = candidate minus reference. All masses in kg. Delta-weight error equals weight error because initial weight is shared. The primary reference disables ECF to compare tissue + glycogen. JSON also reports full Hall ECF-inclusive scale weight. Neither reference models resistance-training preservation/gain or macro-specific TEF. These are model-agreement scores, not human accuracy.

Reference trajectories below the app's essential-fat floor: 0 / 4096. These remain in scores; no post-hoc exclusions. Strength, surplus, unknown-carb and floor cases can legitimately disagree. Plateau proxy is error in the final week's change at each horizon; 24 weeks is not necessarily equilibrium.

| Engine | Weeks | Weight MAE | Bias | P95 | Fat MAE | Lean MAE | Water MAE | ECF-inclusive MAE | Last-week change MAE |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| legacy | 1 | 0.254 | 0.126 | 0.703 | 0.039 | 0.047 | 0.302 | 0.702 | 0.254 |
| legacy | 2 | 0.266 | 0.107 | 0.717 | 0.047 | 0.086 | 0.308 | 0.690 | 0.046 |
| legacy | 4 | 0.299 | 0.111 | 0.783 | 0.070 | 0.161 | 0.310 | 0.692 | 0.035 |
| legacy | 8 | 0.384 | 0.150 | 1.026 | 0.131 | 0.304 | 0.307 | 0.738 | 0.034 |
| legacy | 12 | 0.482 | 0.189 | 1.301 | 0.195 | 0.440 | 0.306 | 0.805 | 0.032 |
| legacy | 24 | 0.788 | 0.295 | 2.013 | 0.397 | 0.823 | 0.305 | 1.021 | 0.028 |

## Stratification at 24 weeks

| Stratum | Engine | n | Weight MAE | Bias | Fat MAE | Lean MAE |
|---|---|---:|---:|---:|---:|---:|
| BMI20-25 | legacy | 1174 | 0.860 | 0.045 | 0.499 | 1.061 |
| BMI25-30 | legacy | 1145 | 0.744 | 0.294 | 0.362 | 0.793 |
| BMI30-38 | legacy | 1777 | 0.769 | 0.461 | 0.351 | 0.685 |
| Female | legacy | 2048 | 0.791 | 0.370 | 0.264 | 0.835 |
| Male | legacy | 2048 | 0.785 | 0.219 | 0.529 | 0.811 |
| high-activity | legacy | 1677 | 0.750 | 0.228 | 0.422 | 0.814 |
| known-carbs | legacy | 3072 | 0.810 | 0.235 | 0.396 | 0.856 |
| large-deficit | legacy | 610 | 0.480 | 0.028 | 0.313 | 0.200 |
| long-horizon | legacy | 4096 | 0.788 | 0.295 | 0.397 | 0.823 |
| maintenance | legacy | 683 | 0.403 | 0.354 | 0.152 | 0.062 |
| moderate-deficit | legacy | 731 | 0.867 | 0.494 | 0.310 | 0.827 |
| no-strength | legacy | 2730 | 0.519 | -0.221 | 0.429 | 0.664 |
| small-deficit | legacy | 708 | 0.945 | 0.864 | 0.295 | 0.659 |
| strength | legacy | 1366 | 1.327 | 1.327 | 0.332 | 1.141 |
| surplus | legacy | 1364 | 0.996 | -0.017 | 0.655 | 1.566 |
| unknown-carbs | legacy | 1024 | 0.722 | 0.476 | 0.399 | 0.724 |

Full per-horizon strata and maxima: `FORECAST_V3_COMPOSITION_BENCHMARK.json`. Sodium/ECF: **NO-GO for production**. The isolated research overlay is tested, but absolute sodium without a measured baseline does not identify a change. A comparison to another model is insufficient evidence to fit hydration or enable ECF.
