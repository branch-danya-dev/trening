# Anatomical muscle fields — Stage B research v1

**NO-GO for changing the default provider.** The bounded anatomical research path is implemented and versioned. The default remains `muscle-field-procedural-1`; existing geometry is preserved. The five-view audit does not establish a material plausibility improvement, posterior torso coverage is weak, and the pinned metadata has no clean latissimus dorsi equivalent. Integration is not evidence of human accuracy.

Base: PR #27 merged normally as `033f9e753be156a51d74b22a832f467989068eb2`; [full main CI](https://github.com/branch-danya-dev/trening/actions/runs/37969028741) passed before this branch. The separate Pages build passed but its deploy returned 404 (Pages configuration); that is not a build/test failure.

## Data and ownership

[Mapping inventory](BODYPARTS3D_MAPPING.md), [pinned manifest](assets/bodyparts3d/manifest.json), [attribution](THIRD_PARTY_ASSETS.md). IS-A Release 4.0 supplies 58 selected muscle objects and 17 skeletal reference objects. The complete archive is deliberately not committed. There are 13 direct internal group mappings, explicit procedural lats fallback, and six secondary no-direct-field groups. Representation IDs (`BP...`) are not element filenames (`FJ...`).

The asset is an anatomical shape prior. It never supplies factual user muscle mass, growth rate, EMG, body-fat percentage or a personalized internal scan. `MuscleAdaptationForecast`, `MuscleLoadEngine`, regional lean allocation, `BodyShapeForecast`, numerical composition and heatmap semantics are unchanged. Geometry selection only changes the displacement provider. The source OBJ volume comments are never read as mass or used for normalization.

## Offline build and reproduction

Download the exact archive intentionally from the manifest URL, then run from the repository root:

```text
dotnet run -c Release --project tools/WorkoutCalculator.AnatomyBuild -- . /path/to/isa_BP3D_4.0_obj_99.zip /path/to/output
dotnet run -c Release --project tools/WorkoutCalculator.AnatomicalMorphBenchmark -- . /path/to/evidence
ANATOMY_OUTPUT=/path/to/evidence APP_URL=http://127.0.0.1:5256 node tests/browser/anatomical-muscle-audit.cjs
ANATOMY_OUTPUT=/path/to/evidence APP_URL=http://127.0.0.1:5256 node tests/browser/anatomical-muscle-smoke.cjs
```

CI reads the committed 27,918-byte sidecar and never downloads anatomy. Parser/archive fixtures test the external boundary; runtime tests verify the pinned mapping/archive/topology/sidecar hashes and deterministic binary encoding. Two intentional full-source builds must match before updating the checked asset. Source hash: `40665852C49F218326590E204DB91064A1ECFC3C6F8CBD7BBBCAAC62C7CD409E`. Derived hash: `A27839A20F2FE1A67AADB9F85A2C608F64DB5512351F6331E9FD492F8FA30F0D`.

The archive reader checks SHA before ZIP access, exact root/filename structure, duplicate IDs, bounded entries/expanded object sizes, finite coordinates, line/vertex/face limits and valid OBJ indices. It never extracts paths to disk or resolves materials. Negative indices and polygon fan triangulation are supported. Exact duplicate positions are welded, degenerate faces removed, and connected components counted. Open/disconnected surfaces are accepted: no signed internal muscle volume or watertight assumption is needed. Build output records component counts (315 across the selected meshes after welding), not an assertion of biological compartments.

## Coordinate registration v1

Source: millimetres, X anatomical left, Y posterior, Z superior. Normalize to metres with `(x,z,-y)/1000`, a handedness-preserving axis rotation. Left and right anatomy are folded into positive X and sampled together; mirrored topology pairs average the resulting vectors. MakeHuman uses metres, X left, Y superior, Z anterior. Canonical hm08 body floor is −0.8167600036 m, top 0.8491299748 m, reference mesh height 1.6658899784 m. Its native origin is retained while building displacement vectors; the existing runtime fit places the personal body floor at zero and applies personal height. Displacement amplitude is defined separately at 175 cm.

Initial similarity registration solves scale and translation from six bone-derived correspondences (hip/knee/ankle/shoulder/elbow/wrist) after axis normalization. The [build report](evidence/anatomical-muscles/build.json) records those source/target points and pre-segment RMS error (~107 mm, reflecting different poses/proportions). A single global transform is insufficient.

Upper arm, forearm reference, thigh and calf then use local orthonormal bone frames. Proximal/distal source landmarks are centroids of the end 10% slabs of the humerus/femur/tibia/radius, averaged across sides; knee uses both adjacent bones. Target landmarks come from the MakeHuman skeleton. Frame length scales longitudinal and radial components. Torso/pelvis use hip-to-shoulder height/width interpolation and sacrum-to-first-thoracic-vertebra depth alignment against the reference spine. Clavicle, hip bone and additional vertebrae remain in the checked inventory for auditing, not claimed as independent fitted constraints.

Segment endpoints coincide by construction; that is not an independent registration accuracy result. Bone-slab landmarks are approximations, not manually annotated joint centres. There is no clinical/held-out registration accuracy claim. The deterministic canonical registration is shared across sexes; personalization remains the existing surface model. v1 only height-scales the cached vectors at runtime; changes of limb proportion are represented by the fixed topology correspondence, not a new personalized internal muscle reconstruction.

## Surface projection algorithm v1

For each direct group, register its selected surfaces. Compute an object centroid and principal belly axis using 32 deterministic covariance iterations; preserve all triangles. A static atlas membership is a broad anatomical candidate mask with smooth boundary fade. It is never the live load heatmap or its colors.

For each candidate skin vertex, find the exact closest point on the selected muscle triangles offline. Derive direction from the muscle's medial principal-axis line toward skin, blend 20% of the nearest surface normal only when it agrees, and project limb directions perpendicular to the bone axis. Require agreement with the body outward normal. No `skinNormal * heatmap` displacement is used. Distance fades to zero at 100 mm; principal-axis end zones fade over 15% of object extent. Joint centres have a 25 mm zero core and 35 mm transition; limb end zones add longitudinal fade. The smooth atlas boundary avoids abrupt field edges.

Any head/neck/jaw/eye/hand/finger/thumb/foot/toe skin influence excludes a vertex. A conservative central perineal region is also excluded. After the full solver, anatomical builds restore protected areas against the same unlayered body and remeasure girths. Thus global fitting cannot move protected vertices indirectly.

## Amplitude, volume and girths

`RelativeGrowth` remains [−0.25,+0.25]. Each quantized basis represents a maximum reference state of +0.25 at 175 cm. Apply `basis * RelativeGrowth/.25 * heightCm/175`; negative states reverse it. Nominal coefficient is 4 mm before envelopes, not the previous 8 mm cap. Combined displacement is bounded at 6 mm scaled by height. The coefficient sweep of 0.5/1/2/4/6 mm is recorded in the build report: 6 mm produces reference triangle inversions; 4 mm passes the positive/negative individual and combined checks after spatial fade. This is visual safety calibration, not a muscle-growth law.

The surface displacement integral `sum(area * displacement dot outward normal)` is a first-order volume proxy. Per-group maximum-state proxy is capped at 0.2 litres before quantization, linearly below that state. It is deliberately not tissue mass. Per-case metrics report requested relative growth, allocated lean kg for real forecast-program cases, raw flux/mesh volume, final body volume and associated girths. Isolated synthetic states have no fabricated allocated mass.

The existing girth/total-volume solver runs after local fields. Known/predicted profile targets keep authority: if an anatomical result worsens any target by more than 0.1 cm versus the same no-field baseline, the local layer is dropped with a limitation. Protected restoration and new triangle inversions trigger the same conservative path. The threshold was not relaxed. No forecast number changes to fit geometry. Final triangle checks reject orientation loss against baseline; edge stretch/triangle metrics are proxies and do not prove absence of all self-intersections.

## Runtime and frozen replay

`IMuscleMorphFieldProvider` keeps the unchanged procedural implementation alongside `AnatomicalMuscleFields`. The binary `AMF1` header has format version, vertex count, topology SHA (canonical positions + indices), source archive SHA, mapping SHA, registration/algorithm versions, 175 cm reference height and ordered group IDs. Sorted 9-byte records are uint16 vertex, uint8 group, three int16 micrometre components. A trailing SHA-256 covers header and payload; a separately pinned whole-file hash prevents accepting substituted content. Browser code only loads/validates/applies this sidecar. No OBJ, nearest surface, registration or per-frame geometry build runs there.

Optional `MuscleGeometry` metadata is frozen into new ForecastSnapshot and EndpointGeometry records. Absent metadata means procedural v1 and is omitted on serialization, preserving historical hashes. New defaults explicitly pin procedural v1. Research fixtures opt into `AnatomicalAsset.Selection`; there is intentionally no production default switch while NO-GO. Existing observed Hypotheses are composition-only and keep identity muscle state; this stage does not invent a strength program or grant regional-mass capabilities.

The saved descriptor contains provider version and exact sidecar hash, while the endpoint already freezes morph state. `AvatarBuilder.BuildEndpoint` verifies the descriptor hash and refuses a different/unavailable asset for exact replay. Interactive forecast display falls back to procedural/disabled local field with a visible warning. Current factual Avatar, CheckIn reconstruction and animations load independently. A later asset update must receive a new version and retain earlier assets to replay their endpoints; changing a pinned hash under an issued version is prohibited.

## Evidence and decision

[Benchmark evidence](evidence/anatomical-muscles/README.md): 488 cases, 19 five-view visual comparisons, protected displacement zero, maximum extra girth residual 0.0964 cm, zero raw/final new inversions after calibration, 24 anatomical fallback cases. Geometry tests cover identity, symmetry, spatial differentiation, both signs, caps, protected regions, finite values, mass invariants, corrupt/missing assets and frozen serialization. Browser checks exercise saved provider selection, colors, rest-mesh animation, week switches, backup, failure fallback and layouts. Existing GeometryWarp and full browser regression remain separate required gates.

**Default remains NO-GO:** visual comparisons are subtle and do not prove improved plausibility; lats have no clean source mapping and posterior torso field coverage remains weak. A real longitudinal-human study, independent registration audit and physical-phone performance test are still separate work. No photorealistic renderer, server/GPU/API, DeltaShape, Pseudo-DXA, new physiology or coach is introduced.
