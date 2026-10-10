# Pre-UI evidence and reproduction

All fixtures and photos in this directory are synthetic. They do not establish human predictive accuracy. Measurements were collected on 2026-10-10; local browser runs used Windows desktop Edge 154.0.4258.62, a 390x844 viewport and Release WASM. CI independently runs Chromium on Ubuntu. Neither is a physical low-end phone.

## Canonical mixed backup

`pre-ui-full-lifecycle.zip`, 525821 bytes, SHA-256:

`581502e9cc27c4622b2f7365558f6263e20d62231be61b68d27836534c597f80`

Public fixture PIN: `1234`. The archive includes nine local stores, a locked avatar and revisions/cycle, nutrition/activity/strength/cardio, evaluated and expired hypotheses, a factual check-in, encrypted synthetic photo pair and GeometryWarp artifact. `mixed-backup.json` lists exact-restore/replay checks.

Import only in a new disposable browser context on a loopback development URL. Before opening the backup file, explicitly set sessionStorage `trening:dev-fixtures=enabled` in that context. The archive's `syntheticFixture:true` flag rejects ordinary production import. Do not enable research mode. The normal Profile backup inspection/confirmation flow then validates and restores it; PIN unlock is separate. Never mix it with personal data. This mode is an accidental-import guard, not protection against deliberate archive editing.

To regenerate, follow the Release app and fixture commands in [TESTING](../../TESTING.md), then run `tests/browser/pre-ui-mixed.cjs` with `GEOMETRY_FIXTURES` and `PRE_UI_OUTPUT`. Regeneration uses the real commands and new issued IDs, so it produces semantically equivalent scenarios, not necessarily this ZIP's exact bytes. The committed ZIP is the exact replay reference.

## Measurement boundaries

- `before-dense-browser.json` and `after-dense-browser.json`: matched original dense archives before and after the first cache/reference/bridge fixes. The baseline 365-day run timed out; there is no successful 1000-day baseline. This dense 1000-day archive exceeds localStorage capacity.
- `history-browser.json` and `history-budgets.json`: final representative 0/30/180/365/1000-day curve, with daily facts and days, check-ins every 28 days, hypotheses every 180 days plus a recent one. Single sequential runs, not statistical latency guarantees. CheckIn has no photo gateway work. Diagnostic logs record its internal stages.
- Browser `restoreValidation` measures ZIP structure/checksum validation inside the browser. `restoreValidationWithAutomationTransfer` additionally includes Playwright's large byte-array transfer and is not application latency. `restoreFullInspection` measures the actual file-input inspection including .NET domain/reference checks, ending at the replacement confirmation.
- `native.json`: portable .NET commands over the same generated histories, including eligible hypothesis preview/issue and cold factual store validation. Memory-backed persistence excludes IndexedDB, WASM bridge and rendering costs. First-use/JIT effects make tiny single timings unsuitable for direct device promises.
- `storage-faults.json`: actual browser IndexedDB fault injection, external WAL crash/rollback/recheck, atomic photo failure and export/delete availability. Unit tests separately cover a second rollback failure and legacy journal support.
- `fixtures-accessibility.json`: 16 developer states, labels/names/IDs and targeted keyboard/focus checks. This is a technical baseline, not a full accessibility certification.
- `pwa-transition.json`: two old controlled clients, waiting update, preserved input, ignored historical forced-activation message, offline old/new cache and activation after both old clients close.
- `dependencies.json` and `security-audit.json`: advisory query and scoped repository/library checks; see [audit scope and license notices](../../PRE_UI_DEPENDENCY_AUDIT.md).

The full PR CI uploads fresh reports and its own canonical fixture as `pre-ui-stabilization-evidence`. The readiness report identifies the tested implementation SHA and run. Local numbers remain labeled local; CI timings are not substituted for them.
