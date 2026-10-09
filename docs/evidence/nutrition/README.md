# Nutrition v1 evidence — 2026-10-09

Baseline: `0f943cc4fdd637d30d71f7d7519bb0aaf0d8e05c` after ordinary merges of #23 then #19, with successful full CI on each resulting main. The nutrition branch was created only after the second main CI passed. No conflict resolution, rebase or squash.

Local Release build: **0 warnings, 0 errors**. **531/531 .NET**, **58/58 JS**. Both Hall benchmark reports reproduce the existing 4096-scenario fingerprint. All twelve development browser suites passed: skeletal, strength, history, forecast, composition, muscle-forecast, product, avatar, avatar-creation, activity-day, nutrition, errors. Published `/trening/` offline smoke also passed, including food entry and frozen partial calories-only totals after reload: **13/13 suites**. CI uses the same thirteen-suite matrix including offline.

[Nutrition report](report.json), [physical ActivityDay regression report](activity-regression.json). Nutrition smoke checks the exact 350→175 half example, second per100g entry, comma input, edits, physical coexistence, explicit coverage, frozen state, stale-tab rejection, RestDay with food, MissingData without zero intake, reload, byte-exact clean backup/restore and allowlisted pseudonymized export. Geometry UUID is unchanged during meal operations. Browser errors: 0.

| Width | Editor | Close Day review |
|---|---|---|
| 320 | [image](editor-320.png) | [image](review-320.png) |
| 390 | [image](editor-390.png) | [image](review-390.png) |
| 1400 | [image](editor-1400.png) | [image](review-1400.png) |

Document and panel widths have no horizontal overflow. Long element captures can include the application's fixed navigation bar; fields/buttons remain reachable by normal scrolling. Native labelled controls support keyboard input and decimal comma/dot.

Windows desktop Edge/SwiftShader, .NET 10.0.401 SDK, single end-to-end sample from `report.json`:

| Action | ms |
|---|---:|
| Open today | 268 |
| Save first meal | 78 |
| Save second food entry | 49 |
| Edit consumed grams | 57 |
| Close, actual click → rendered Completed state | 59.3 |
| Close, entire automation action including actionability/scroll | 2060 |
| Clean restore + navigation + model ready | 2946 |

These are not microbenchmarks or physical-phone measurements. The separate physical activity smoke measured close at 115 ms and daily heatmap at 188 ms, with unchanged geometry. Automation overhead differs between scenarios, so the 2060 ms close action is not claimed as processing latency or compared as a like-for-like regression. No speedup is claimed. CI runs independently on Linux/Chromium and uploads the same nutrition report/screenshots artifact.

Known limits: a physical low-end phone is unmeasured; coverage relies on user confirmation; unsaved editor fields are transient; no reopen/amend, food catalog/provider, recipe engine, barcode, automatic coach, Hypothesis 14/30, CheckIn or renderer. Sodium/ECF remains production NO-GO. The existing disabled GitHub Pages deployment is separate from the successful build/test/offline CI and was not changed.
