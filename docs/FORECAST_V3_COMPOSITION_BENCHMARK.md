# Forecast v3 composition benchmark (generated)

Reference `hall-2011-appendix-rk4-1`; baseline main `471449780756f091234849d3055bd1e149f9312e`.
4096 scenarios; LCG seed 20261009; scenario SHA-256 `69ACB731B555A862EE2F2EFC1BD201FABF700372A769E8C376E8E234BBEA56C8`.

Reproduce: `dotnet run -c Release --project tools/WorkoutCalculator.CompositionBenchmark -- --check`. Use `--write` to regenerate. No network is used. See tools/WorkoutCalculator.CompositionBenchmark/REFERENCE.md for scope, equations and assumptions.

Errors = candidate minus reference. All masses in kg. Delta-weight error equals weight error because initial weight is shared. The primary reference disables ECF to compare tissue + glycogen. JSON also reports full Hall ECF-inclusive scale weight. Neither reference models resistance-training preservation/gain or macro-specific TEF. These are model-agreement scores, not human accuracy.

Reference trajectories below the app's essential-fat floor: 0 / 4096. These remain in scores; no post-hoc exclusions. Strength, surplus, unknown-carb and floor cases can legitimately disagree. Plateau proxy is error in the final week's change at each horizon; 24 weeks is not necessarily equilibrium.

| Engine | Weeks | Weight MAE | Bias | P95 | Fat MAE | Lean MAE | Water MAE | ECF-inclusive MAE | Last-week change MAE |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| composition-v3 | 1 | 0.055 | -0.010 | 0.169 | 0.022 | 0.051 | 0.024 | 0.491 | 0.055 |
| composition-v3 | 2 | 0.097 | -0.011 | 0.266 | 0.041 | 0.094 | 0.032 | 0.504 | 0.046 |
| composition-v3 | 4 | 0.165 | 0.004 | 0.418 | 0.081 | 0.175 | 0.035 | 0.538 | 0.035 |
| composition-v3 | 8 | 0.287 | 0.046 | 0.714 | 0.163 | 0.324 | 0.032 | 0.613 | 0.031 |
| composition-v3 | 12 | 0.400 | 0.087 | 0.993 | 0.245 | 0.464 | 0.030 | 0.692 | 0.029 |
| composition-v3 | 24 | 0.703 | 0.195 | 1.754 | 0.496 | 0.853 | 0.026 | 0.929 | 0.025 |
| legacy | 1 | 0.254 | 0.126 | 0.703 | 0.039 | 0.047 | 0.302 | 0.702 | 0.254 |
| legacy | 2 | 0.266 | 0.107 | 0.717 | 0.047 | 0.086 | 0.308 | 0.690 | 0.046 |
| legacy | 4 | 0.299 | 0.111 | 0.783 | 0.070 | 0.161 | 0.310 | 0.692 | 0.035 |
| legacy | 8 | 0.384 | 0.150 | 1.026 | 0.131 | 0.304 | 0.307 | 0.738 | 0.034 |
| legacy | 12 | 0.482 | 0.189 | 1.301 | 0.195 | 0.440 | 0.306 | 0.805 | 0.032 |
| legacy | 24 | 0.788 | 0.295 | 2.013 | 0.397 | 0.823 | 0.305 | 1.021 | 0.028 |
| personalized-v3-zero-evidence | 1 | 0.055 | -0.010 | 0.169 | 0.022 | 0.051 | 0.024 | 0.491 | 0.055 |
| personalized-v3-zero-evidence | 2 | 0.097 | -0.011 | 0.266 | 0.041 | 0.094 | 0.032 | 0.504 | 0.046 |
| personalized-v3-zero-evidence | 4 | 0.165 | 0.004 | 0.418 | 0.081 | 0.175 | 0.035 | 0.538 | 0.035 |
| personalized-v3-zero-evidence | 8 | 0.287 | 0.046 | 0.714 | 0.163 | 0.324 | 0.032 | 0.613 | 0.031 |
| personalized-v3-zero-evidence | 12 | 0.400 | 0.087 | 0.993 | 0.245 | 0.464 | 0.030 | 0.692 | 0.029 |
| personalized-v3-zero-evidence | 24 | 0.703 | 0.195 | 1.754 | 0.496 | 0.853 | 0.026 | 0.929 | 0.025 |

## Stratification at 24 weeks

| Stratum | Engine | n | Weight MAE | Bias | Fat MAE | Lean MAE |
|---|---|---:|---:|---:|---:|---:|
| BMI20-25 | composition-v3 | 1174 | 0.812 | -0.009 | 0.617 | 1.091 |
| BMI25-30 | composition-v3 | 1145 | 0.687 | 0.201 | 0.479 | 0.820 |
| BMI30-38 | composition-v3 | 1777 | 0.642 | 0.327 | 0.427 | 0.717 |
| Female | composition-v3 | 2048 | 0.732 | 0.398 | 0.451 | 0.840 |
| Male | composition-v3 | 2048 | 0.675 | -0.007 | 0.540 | 0.866 |
| high-activity | composition-v3 | 1677 | 0.703 | 0.176 | 0.532 | 0.852 |
| known-carbs | composition-v3 | 3072 | 0.742 | 0.259 | 0.549 | 0.896 |
| large-deficit | composition-v3 | 610 | 0.541 | -0.224 | 0.308 | 0.209 |
| long-horizon | composition-v3 | 4096 | 0.703 | 0.195 | 0.496 | 0.853 |
| maintenance | composition-v3 | 683 | 0.504 | 0.445 | 0.279 | 0.268 |
| moderate-deficit | composition-v3 | 731 | 0.753 | 0.452 | 0.330 | 0.804 |
| no-strength | composition-v3 | 2730 | 0.525 | -0.232 | 0.543 | 0.665 |
| small-deficit | composition-v3 | 708 | 0.613 | 0.552 | 0.355 | 0.652 |
| strength | composition-v3 | 1366 | 1.059 | 1.049 | 0.401 | 1.228 |
| surplus | composition-v3 | 1364 | 0.897 | -0.065 | 0.850 | 1.565 |
| unknown-carbs | composition-v3 | 1024 | 0.587 | 0.004 | 0.336 | 0.723 |
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
| BMI20-25 | personalized-v3-zero-evidence | 1174 | 0.812 | -0.009 | 0.617 | 1.091 |
| BMI25-30 | personalized-v3-zero-evidence | 1145 | 0.687 | 0.201 | 0.479 | 0.820 |
| BMI30-38 | personalized-v3-zero-evidence | 1777 | 0.642 | 0.327 | 0.427 | 0.717 |
| Female | personalized-v3-zero-evidence | 2048 | 0.732 | 0.398 | 0.451 | 0.840 |
| Male | personalized-v3-zero-evidence | 2048 | 0.675 | -0.007 | 0.540 | 0.866 |
| high-activity | personalized-v3-zero-evidence | 1677 | 0.703 | 0.176 | 0.532 | 0.852 |
| known-carbs | personalized-v3-zero-evidence | 3072 | 0.742 | 0.259 | 0.549 | 0.896 |
| large-deficit | personalized-v3-zero-evidence | 610 | 0.541 | -0.224 | 0.308 | 0.209 |
| long-horizon | personalized-v3-zero-evidence | 4096 | 0.703 | 0.195 | 0.496 | 0.853 |
| maintenance | personalized-v3-zero-evidence | 683 | 0.504 | 0.445 | 0.279 | 0.268 |
| moderate-deficit | personalized-v3-zero-evidence | 731 | 0.753 | 0.452 | 0.330 | 0.804 |
| no-strength | personalized-v3-zero-evidence | 2730 | 0.525 | -0.232 | 0.543 | 0.665 |
| small-deficit | personalized-v3-zero-evidence | 708 | 0.613 | 0.552 | 0.355 | 0.652 |
| strength | personalized-v3-zero-evidence | 1366 | 1.059 | 1.049 | 0.401 | 1.228 |
| surplus | personalized-v3-zero-evidence | 1364 | 0.897 | -0.065 | 0.850 | 1.565 |
| unknown-carbs | personalized-v3-zero-evidence | 1024 | 0.587 | 0.004 | 0.336 | 0.723 |

## Regressions and interpretation

All-horizon weight MAE can improve while tissue partition worsens. The fixed-TEF Hall oracle cannot independently validate macro TEF, and it has no strength hypertrophy mechanism. No parameters were fitted to these report outcomes. Positive differences below mean worse agreement (v3 minus legacy). Personalized v3 uses zero evidence here; nonzero response fitting is tested on strictly prior synthetic observations, not on this oracle.

| Weeks | Weight MAE difference | Fat MAE difference | Lean MAE difference |
|---:|---:|---:|---:|
| 1 | -0.198 | -0.017 | 0.004 |
| 2 | -0.170 | -0.006 | 0.008 |
| 4 | -0.134 | 0.011 | 0.014 |
| 8 | -0.098 | 0.032 | 0.020 |
| 12 | -0.082 | 0.050 | 0.024 |
| 24 | -0.085 | 0.099 | 0.030 |

24-week weight regressions by stratum:
- large-deficit: 0.480 → 0.541 kg MAE.
- maintenance: 0.403 → 0.504 kg MAE.
- no-strength: 0.519 → 0.525 kg MAE.

Full per-horizon strata and maxima: `FORECAST_V3_COMPOSITION_BENCHMARK.json`. Sodium/ECF: **NO-GO for production**. The isolated research overlay is tested, but absolute sodium without a measured baseline does not identify a change. A comparison to another model is insufficient evidence to fit hydration or enable ECF.
