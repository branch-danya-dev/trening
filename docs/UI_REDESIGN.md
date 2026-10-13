# UI/UX Redesign V1

`UI_REDESIGN_BASELINE = bf2586681f6e3369cd6f45c3e25a969d6c85b077`

PR #31 was checked open, mergeable, and at the expected head `3f7d9cbf7d528aa32a9679af9e5016990ad35ce6`. Its [exact-head CI](https://github.com/branch-danya-dev/trening/actions/runs/38049072180) succeeded. It was merged with an ordinary merge commit. The [full new main CI](https://github.com/branch-danya-dev/trening/actions/runs/38366073662), including published offline PWA and version transition, succeeded before `codex/ui-redesign-v1` was created on 2026-10-13. No squash or rebase.

## Product composition

| Zone | Before | After |
|---|---|---|
| Today | Calendar and plan first, repeated heatmaps | Date/state, quick add, factual summary, activity, nutrition, plan comparison, focused close-day review; disclosed calendar/load |
| Model | Provider switch in header, workout/calibration dashboard | Hero 3D, factual metrics/date, new measurement, secondary correction, stored previous/current avatar comparison; provider in Advanced |
| Progress | Measurement editor/history before explanation | Period and source-labelled trend, headline changes, current/historical 3D, single CheckIn path, photos and forecast outcome |
| Plan | Observed forecast and manual scenario competing | 14/30-day observed hypothesis first, evidence progress, maturity, frozen endpoint, target CheckIn; separate disclosed manual scenarios |
| Profile | Long validation/debug output among everyday controls | Personal data, privacy/photos, storage health and backup; diagnostics/versions disclosed |

The eight-stage onboarding lifecycle is preserved, with a step indicator, optional-field guidance, factual review, live correction and explicit final lock. CheckIn uses the same application service, shows progress during processing, and reports factual persistence separately from avatar acceptance. Hypothesis outcome attachment remains explicit and idempotent within that flow. Failed photo quality does not discard permissible factual inputs.

## Presentation architecture

`ui-v1.css` defines the V1 tokens and responsive composition over existing feature styles. `NavIcon`, `ProgressOverview`, `StorageHealthPanel`, `Home.Ui` and `UiError` are presentation components/adapters. Charts consume the already validated history model. Avatar comparison calls the existing version-aware `AvatarBuilder.Rebuild` on frozen revisions and caches the pair; it does not create revisions or facts. Camera framing accounts for both upper and lower UI controls without changing meshes or physiology.

Desktop uses permanent left navigation and a large viewer beside state/actions. Mobile uses five bottom destinations with safe-area padding, stacked editors and 44px interaction targets. Tested viewport targets: 320, 390, 768, 1024 and 1400 px. No account, cloud service or hidden upload is introduced.

## Integrity boundary

`PRE_UI_CONTRACT.md` remains authoritative. Domain, model, storage envelopes, hashes, CAS, lifecycle/eligibility, calibration, journal recovery and historical replay are not redesigned. UI retains the existing application/store commands and renderer capability policies. No backend, accounts, cloud sync, AI renderer, DeltaShape, Pseudo-DXA, new physiology or participant recruitment is implemented.

## Validation and acceptance

The implementation is isolated on `codex/ui-redesign-v1`. The exact-head GitHub CI and final GO decision are linked in the PR; this branch must not auto-merge. Local validation covers Release (0 warnings/errors), 683 .NET, 93 JS, 34 Python, the existing behavioral browser suites and published offline/update scenarios. CI repeats the full matrix and generates its own gallery artifact.

The focused UI smoke covers 320/390/768/1024/1400, five named destinations, one selected ARIA tab, keyboard arrows/focus, touch targets, unique IDs/labels, no page overflow, closed secondary sections, absence of engineering metadata in ordinary screens, quick actions, close-day review focus, durable CheckIn, byte-preserving stored-avatar comparison, offline messaging and saving facts when WebGL is unavailable. Existing test assertions about geometry, journal bytes, eligibility, immutable forecasts, CAS, independent outcomes and exact backup/restore remain intact. Helpers open the real disclosure controls before exercising secondary features; screenshots remain separate from behavioral assertions.

The synthetic gallery includes 73 captures: major states at 390/1400, and critical forms, outcomes and recovery at 320. It includes accepted/rejected photo CheckIn, supported/unsupported local GeometryWarp, large history, quota, corrupt storage, offline and unavailable WebGL. The before directory retains the baseline screenshots and timing reports. No private data or participant images are used.

## Performance

Measured on the same Windows desktop, Edge 155.0.4283.45, 390px viewport and identical generated archives, without concurrent heavy browser work. Values are single observations, not statistical estimates or promises for physical phones. Full phases and raw logs are in [performance comparison](evidence/ui-redesign/performance-comparison.json).

| History | Startup before → after | Today before → after | CheckIn save before → after |
|---|---|---|---|
| 0 days | 2844 → 2587 ms | 133 → 119 ms | 845 → 830 ms |
| 30 days | 3075 → 3003 ms | 219 → 211 ms | 1215 → 1224 ms |
| 180 days | 4035 → 4093 ms | 636 → 638 ms | 1652 → 1580 ms |
| 365 days | 5728 → 5466 ms | 1172 → 1150 ms | 2196 → 2048 ms |
| 1000 days | 12065 → 10908 ms | 3203 → 2874 ms | 4145 → 3603 ms |

The redesign initially shifted a cold day-history read from the former global reminder into first Today navigation. The UI now preserves the same read-only service warm-up without rendering that reminder on unrelated screens. Removing smooth document scrolling avoids delayed focus in close-day review. No storage validation, CAS or persistence boundary was bypassed. Calendar disclosure is a new, separately measured action (24–60 ms); it is not hidden inside the navigation comparison. No material regression remains in these matched interactions.

At 1000 days CheckIn is 3.60 s versus 4.15 s before. The 1000/fresh ratio is 4.34×; the pre-UI desired ≤2× target remains **unmet**. Whole-envelope serialization and exact-byte storage boundaries remain unchanged. UI shows honest progress and never reports success before the durable service completes.

## Known UX limits

Desktop viewport emulation is not physical low-end-phone or screen-reader certification. Native disclosures reduce visible complexity but some advanced editors, sources and large-history lists are still long. Confirmed-day correction and last successful backup timestamp are not available in the existing contract. Additional conveniences and any empirical pilot need separate work; see [UI_FOLLOWUPS](UI_FOLLOWUPS.md). There is no participant recruitment in this change.

See [audit](UI_UX_AUDIT.md), [design system](UI_DESIGN_SYSTEM.md), [copy glossary](UI_COPY_GLOSSARY.md), and [evidence gallery](evidence/ui-redesign/README.md).
