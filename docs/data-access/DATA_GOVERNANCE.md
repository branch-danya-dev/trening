# Proposed research data governance

Version `research-governance-1`, prepared 2026-10-09. This is a proposed operating plan, not evidence of institutional approval or a signed agreement.

Research data never enter the application user store. A pseudonymous surface mesh can still identify a body: pseudonymisation is not anonymity. Treat raw scans, registered shapes, landmarks, dates, latents, model bases and per-case reports as restricted until the custodian authorises export.

## Access and environment

Prefer the custodian's secure environment. For an expressly permitted local environment: named least-privilege researchers, full-disk encryption, MFA where available, access audit log, no shared account, no consumer backup/sync, no public network service, no external model APIs and a reviewed incident-response contact. Configure the research root outside this repository and web assets. An approved data steward holds the linkage key separately. Store random project pseudonyms only; do not hash real IDs without a secret.

Use custodian-provided participant linkage, with written permission where required, to prevent the same person appearing in different studies/splits. Do not attempt identity matching from faces, bodies or external records. Dates should be consistently shifted per participant or represented by elapsed days while preserving intervals.

## Controls before data ingestion

Record dataset/version, inventory hashes, release/DUA reference, authorised purpose, derivative-training scope, model-commercial/export decision, named steward, storage location, access countries, ethics determination, retention end and deletion procedure in a **private** gate file. A public example contains no approvals or private identifiers and denies all gates. A true boolean is an operator attestation, not legal review; retain the signed evidence privately. Validate gate and inventory before training or held-out evaluation. Synthetic fixtures bypass only through explicit test mode and permanently label their outputs as non-evidence.

Raw datasets, caches and local export packages are gitignored. A CI guard detects common accidental research paths; it is not a DLP guarantee. Review staged files and output manifests before every public commit. Restrict all filenames to the configured root and reject symlinks/path traversal for deletion and import. Bound mesh/JSON/archive sizes. Never execute untrusted dataset content.

## Retention, withdrawal and deletion

Before receipt, agree a concrete retention end and authorised deletion method with the custodian. Do not invent a retention period on their behalf. Delete earlier on revocation unless legal/institutional obligations require a documented hold. Cover raw scans, copies, derived registration, latents, caches, per-case reports and backups. Assess models for retraining/removal according to the agreement and withdrawal procedure. Keep a minimal deletion receipt containing aggregate counts, time and method; no scan contents or identifiers.

The cache deletion tool defaults to a dry run, operates only inside a marked research cache root and refuses symlinks or unmarked directories. It removes registered cache files, not source datasets. Filesystem deletion does not promise secure erasure of SSDs or backups; the steward must handle encrypted volume/key and institutional backup procedures separately.

## Publication and eventual deployment

No participant-level artifacts in public PR evidence. Release only reviewed aggregates with small-cell suppression (default fewer than five participants), code without paths/keys, and model artifacts explicitly permitted by agreement. Attribute each source and report negative findings. Test contamination, pretraining overlaps and loss of independence from DXA-derived shapes must be disclosed.

Commercial use, derivative training, export of weights and redistribution are separate decisions. Do not ship a model unless all required rights and held-out GO gates pass. No raw scans in the shipped PWA. No third-party generative renderer or remote provider is part of this package.
