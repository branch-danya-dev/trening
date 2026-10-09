# Phase 7 evidence — 2026-10-09

Local structural/integration gates passed on Windows, headless Edge 154.0.4258.62, WebGL2/SwiftShader. The implementation PR's complete CI is the final merge gate. Baseline #26 was merged normally as `7eb03abc2c1f36fe6f0ec78a72134b1a3802d962`; [full main CI](https://github.com/branch-danya-dev/trening/actions/runs/37942116786) was green before the implementation branch was created. No auto-merge.

The three new browser scripts explicitly force SwiftShader. An initial Linux run exposed seven holes in the raw identity shader (the production identity copy and all 88 benchmark cases passed). The issue was reproduced locally with forced SwiftShader and traced to sample-center UV rounding being amplified by depth-slope correction. The fix retains the 2 mm visibility threshold and checks direct interpolated depth within 1/16 pixel of a sample center. Assertions remain zero holes and zero identity byte error; failed evidence is not treated as GO. The raw shader test writes its diagnostics before assertions so any future failure remains inspectable.

## Reproducible structural benchmark

[benchmark.json](benchmark.json) records every case, including failures: NoWarp, the actual preserved legacy `PhotoWarp.Plan`/`warpRows` baseline, GeometryWarp metrics, state, runtime and estimated working bytes. The repository audit found this real row renderer, but no existing full-mesh renderer.

Four MakeHuman profiles (Male/Female, standard/large), front/side, eleven changes: identity, waist ±4%, hips/thigh ±4%, chest ±4%, weight/girth loss/gain, small supported shoulder-posture delta, unsafe large growth. Geometry is reconstructed through the existing AvatarBuilder; no new physiology. The source image is a generated checker/shaded body over a fixed background. These are explicitly **synthetic controlled fixtures**, not human photos or empirical human prediction evidence.

| Check | Observed result |
|---|---|
| Total | 88 cases: 40 Accepted, 41 Limited, 7 Rejected |
| Identity | 8/8 Accepted; maximum RGBA byte difference 0; no repair; source/target masks identical |
| Supported deltas | 72/72 Accepted or Limited; target silhouette IoU 1 |
| Improvement against NoWarp | 70/72 strictly improve IoU; 2 small deltas have pixel-identical baseline silhouette (IoU already 1) |
| NoWarp, supported deltas | IoU 0.973623… to 1 |
| Unsafe large growth | 7 Rejected; 1 Limited within the bounded repair/stretch gates |
| Background outside transition band | 0 changed pixels in all 88 cases |
| Holes in accepted/limited images | 0 |
| Estimated buffers at 512×768 | 41,569,248 bytes (~39.6 MiB), not measured process/GPU peak |

The large-growth case allowed as Limited is not evidence that arbitrary large changes are supported: its projected visible texture stayed within the fixed area/distance limits. Rejected cases remain in the report. The legacy baseline is a historical comparison; GO requires improvement against NoWarp, not an assertion that every metric of the legacy renderer always improves.

[webgl.json](webgl.json) separately verifies source-hidden fragments are rejected, target z-occlusion, normal direction, finite depth, missing WebGL2, shader failure and context loss/recovery through a fresh context. **Shader identity without the production identity shortcut** has 0 holes and 0 maximum byte difference. Repeated nonidentity WebGL rendering has 0 maximum byte difference on this backend. Policy tolerance for different runs on the same backend is ≤1 byte; cross-GPU bit identity is not claimed.

The pure JS identity test also checks imperfect source/model alignment: unchanged original pixels retain their actual observed silhouette in metrics; they cannot falsely report a perfect target mask.

## Product, storage and privacy

[smoke.json](smoke.json) exercises actual Blazor/WASM and WebGL: seeded locked Avatar with exact frozen photo provenance → three real UI closed days/meals → issued 14d Hypothesis → Progress → front/side render → download. It then updates current Avatar through a real CheckIn and checks the old core/artifact bytes/metadata stay unchanged.

Additional assertions cover synthetic file re-entry rejection even with OriginalObservation claim, corrupt metadata/output bytes, changed source during render, stale restore-generation writes, PIN encrypt/lock/unlock, full-backup clean restore preserving exact metadata and bytes, source-delete cascade and pseudonymized diagnostics. Render transport requests during generation: **0**. Local `blob:` image display does not send a network payload.

[pwa-smoke.json](pwa-smoke.json) repeats the complete flow on published `/trening/`, then uses the controlled Service Worker to reload the **entire app offline**, unlock the photo and generate a new artifact. The existing general offline smoke also passed. The full synthetic backup with two render PNGs and two original fixture images is about 607 KiB (exact `backupBytes` in each report); it is deliberately not committed. Original/generated image bytes are excluded from ordinary diagnostics.

Per-render reports contain source decrypt/load, current/target rebuild, alignment, maps, warp, composite, encode and artifact preparation timings. They are local desktop observations, not phone latency promises. Transaction wall time is returned at runtime; persisted metadata records preparation before commit. Cached invocation preserves existing result/bytes and currently still reconstructs the two meshes before JS cache lookup.

## Regression and layout

Release build: **0 warnings, 0 errors**. **623 .NET tests, 80 JS tests**, unchanged legacy and composition-v3 4096-scenario benchmark hash `69ACB731B555A862EE2F2EFC1BD201FABF700372A769E8C376E8E234BBEA56C8`.

All existing browser suites passed: skeletal, strength, history, forecast, composition, muscle-forecast, product, avatar, avatar-creation, activity-day, nutrition, hypothesis, checkin, errors and published offline. New geometry benchmark, WebGL checks and product smoke are added to CI; the product smoke also runs against published offline PWA. There are 18 distinct browser scripts and 19 executions including this published repeat. Historical tests now open the current IndexedDB version; their assertions are retained. History fixture uses a controlled creation clock instead of modifying protected source timestamps.

No horizontal overflow at 320/390/1400 px. Screenshots use synthetic fixture images only:

- [Front 320](front-320.png), [front 390](front-390.png), [front desktop 1400](front-1400.png).
- [Side 390](side-390.png), [unsupported source 390](unsupported-390.png), [downloaded synthetic PNG](geometry-front.png).

## Reproduction and remaining limits

Run the commands in [TESTING](../../TESTING.md#phase-7--geometrywarprenderer). Fixture geometry is pinned by the checked-in MakeHuman asset, BodyDefaults and benchmark source; random IDs identify only test archive records and do not affect geometry. CI uploads complete fresh evidence under `geometry-warp-evidence`.

GO is limited to the implemented local structural contract and integration gates. Real-human visual quality, real-photo acceptance rate, loose-clothing false acceptance and empirical forecast accuracy are **unvalidated**. Viewport emulation is not a physical low-end phone test. The v1 canonical pose is strict, has no IK and can reject ordinary photos whose arms or silhouette differ from the frozen model. Old revisions without exact analysis hashes/masks remain 3D-only. External screenshots/re-encoding can remove synthetic markers; these guards are not forensic image detection.

See [GEOMETRY_WARP](../../GEOMETRY_WARP.md) for thresholds, orthographic projection, visibility/repair policies, privacy, retention and a future consented real-photo validation protocol. Phase 8, backend, generative inpainting, BodyParts3D and DeltaShape are not implemented.
