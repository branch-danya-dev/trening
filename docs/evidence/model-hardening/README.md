# Frozen desktop evidence

Generated on 2026-10-09 from clean implementation commit `a3ff06039a226475dbcd7f37c92769503ef4e42f`. Subsequent commits publish documentation/evidence and align the legacy browser fixture with absent pre-registry metadata; application implementation is unchanged. [Provenance](provenance.json) records all successful reproduction commands and SHA-256 of this published report subset and the original reports. Only line endings were normalised to LF for publication. Reproduce with `python tools/reproduce_model_validation.py <fresh-directory>`.

All inputs are synthetic structural fixtures. No participant dataset, C1 fit, Pseudo-DXA ablation or human accuracy result exists here. [Empty prospective report](prospective-empty.json) is `NO_PROSPECTIVE_DATA`. The full .NET/JS/Python suite was separately run on the same implementation: 667 / 84 / 34 passing; Release zero warnings/errors. Full PR CI additionally covers the remaining browser regression and published offline PWA.

## Native domain timings

.NET 10.0.12, Windows x64, 12 logical processors. First call plus 20 subsequent samples; warm median/p95 do not include first call. These include domain computations only; issue/CheckIn exclude persistence. Cold adapter means a new adapter, not cleared OS disk cache. [Complete performance report](performance.json) includes assets/allocations and memory snapshots, which are not peak memory.

| Operation | First ms | Warm median ms | Warm p95 ms |
|---|---:|---:|---:|
| avatar-build-cold-adapter | 65.76 | 14.29 | 16.75 |
| avatar-rebuild-warm-adapter | 3.17 | 2.99 | 3.22 |
| hypothesis-preview-30-days | 151.32 | 11.40 | 12.01 |
| hypothesis-issue-domain-no-storage | 111.30 | 6.69 | 7.40 |
| future-endpoint-procedural | 16.79 | 3.22 | 3.59 |
| full-fit-procedural-muscle | 12.43 | 10.32 | 14.54 |
| full-fit-anatomical-research | 26.14 | 16.15 | 20.26 |
| checkin-process-domain-no-storage | 29.39 | 0.74 | 0.90 |

## Browser startup

Headless desktop Edge, loopback server. Ready time includes application readiness, not a mobile network simulation. JS heap is not total browser/WASM memory. Asset transfer and decoding records are in [startup report](browser-startup.json).

| Sample | Ready ms | JS heap MiB |
|---|---:|---:|
| fresh-context | 2307 | 8.56 |
| warm-reload | 2087 | 10.94 |

## Lifecycle and geometry

[Avatar](avatar.json), [Hypothesis](hypothesis.json) and [CheckIn](checkin.json) reports retain measured browser timings and completed checks. [Geometry benchmark](geometry-benchmark.json), [WebGL](geometry-webgl.json) and [warp lifecycle](geometry-smoke.json) retain numeric gates. [Anatomical summary](anatomical-summary.json) still reports default NO-GO. Large generated fixture meshes, backup ZIPs and screenshots are intentionally not included in this compact subset; these are reproducible and full CI artifacts remain available from the PR run.

Layout checks at 320/390/1400 pixels are not physical device tests. Long-history CheckIn latency, peak browser memory, low-end-phone thermals and all real-person accuracy/perception metrics remain explicit future tests.
