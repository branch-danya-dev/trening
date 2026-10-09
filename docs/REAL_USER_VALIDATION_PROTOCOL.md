# Prospective controlled user validation — prepared, not started

Protocol `prospective-fitness-1`, 2026-10-09. No participants recruited, contacted or enrolled by this work package. This document does not assert ethics approval. The user will separately authorise launch, recruitment, consent and UI observation after reviewing readiness.

## Purpose and boundaries

Test whether adults can complete Profile → Avatar → Activity/Nutrition → locked Hypothesis → CheckIn → Forecast-vs-Fact, and collect prospective error measurements. Numerical and reconstructed body shape are estimates, not medical measurements or promises. A generated image is never an outcome. Use usual self-selected routines; do not prescribe unsafe weight loss or an intervention for this pilot.

## Before enrolment

- Name the accountable study owner, contact and data steward; agree ethics/privacy review appropriate to the jurisdiction, consent wording and lawful basis.
- Define the intended adult population, sample target and uncertainty of estimates with a statistician before making claims. Small convenience pilots test feasibility; they cannot establish broad accuracy.
- Exclude minors, pregnancy/postpartum states where the model is unsuitable, acute fluid changes/illness, recent body-altering surgery, and conditions/interventions outside the documented model support. Do not solicit diagnoses in ordinary exports; record only an eligibility decision privately.
- Pre-register endpoints, inclusion rules, time windows, independent outcome sources, subgroup cells, handling of repeats and missingness. Freeze the app/model version for a cohort and record every subsequent change.
- Supply a plain-language consent covering voluntary participation, limitations, local photo handling, exactly which exports are shared, recipients, storage, retention end and withdrawal route. Separate optional photo sharing from numeric validation consent. Do not bundle consent with core app access.

## Baseline and origin

Record measured height and weight, measurement method and timing. Prefer at least two consistent weight readings under the same morning conditions. Record waist and hip plus optional chest, thigh, arm, neck/calf with a non-stretch tape, specified landmarks, posture and relaxed breathing. Repeat tape measurements twice; retain disagreement/quality rather than silently averaging implausible values. BF and DXA are optional and source-labelled; photo estimates are not BF measurements.

Review the factual/estimated fields and lock an Avatar revision. Collect optional likeness rating (1–5) and task observations without converting them into geometric accuracy. Log correction use and reconstruction residuals; these are fitting diagnostics and not independent validation of the measurements used to fit the Avatar.

For optional photos use front and side, same camera/lens/distance/height, neutral stance, whole body in frame, consistent lighting/background and fitted clothing. Retain originals only locally by default. Avoid other people, identifying background and unnecessary face detail. A back photo can be a separately consented research reference, not a supported production reconstruction requirement. No external photo transfer by default.

## Prospective cycles

Use 14- or 30-day product hypotheses, locked before any outcome is observed. Collect optional 4/8/12-week research checkpoints separately; a 30-day endpoint is not a 28-day endpoint. Do not backdate or recalculate a frozen endpoint after seeing the outcome. Record origin creation time, evidence cutoff, exact target date, provider/asset versions, range policy and origin quality.

Ask participants to record daily nutrition and activity honestly and to distinguish complete, partial, explicit rest and missing days. Report coverage rather than treating missing entries as zero. The existing product gate can issue preliminary hypotheses; report that maturity separately. Do not require calorie precision the participant cannot provide.

At each product target, record the outcome ideally on the exact day; the existing product window is target −1 through +3 days. Retain the actual horizon and timing category and analyse exact-day outcomes as primary, early/late as sensitivity. Use the same measurement protocol and independent scale/tape readings. Save before comparing to the forecast where practical. Optional factual Avatar updates retain revision quality and fit diagnostics, not a claim of measured 3D surface change.

Manual reset/recalibration starts a new tracking origin; previous hypotheses remain frozen. Retrospective/reconstructed forecasts, outcome-known origins, generated images, non-original photos, invalid measurements and missing endpoints are excluded from prospective accuracy. Preserve exclusion counts and reasons. Do not select only successful or adherent completers.

## Export and analysis

Participant opts in to a previewed validation package. The coordinator assigns a random study pseudonym outside the app and maintains the consent/withdrawal key separately. No names, local IDs, notes or photos in the default numeric package. Relative references link hypotheses, origins, check-ins and revisions inside a package; exact dates/body measurements still need restricted handling. The analysis intake manifest maps each package to an assigned participant and enforces one current package per participant to avoid double-counting repeated exports.

Aggregate only eligible prospective rows. Report actual-minus-predicted bias, MAE and counts by metric, actual horizon and origin/provider version; observed heuristic range coverage with its denominator; outcome missingness, source quality, nutrition/activity coverage and exclusion counts. First aggregate within participant so prolific users do not dominate. Subgroups use sex/age/BMI bands from **baseline**, with small cells suppressed (default <5). No accuracy percentage. Range coverage is an observed frequency, not a confidence probability.

Report geometry comparisons only when forecast and factual revisions can be linked and compatible independent geometry exists. Fitted girth residuals and manually corrected Avatar likeness remain separate from surface ground truth. No photo/mesh export is added implicitly. Report unavailable geometry metrics as missing, not zero.

Synthetic test fixtures exercise analysis arithmetic and exclusions in a separate test run. They never populate the human validation report or train empirical calibration. A future report with no real intake says `NO_PROSPECTIVE_DATA`.

## Withdrawal, security and retention

Before launch set a concrete retention end, approved recipients and deletion contact in the consent. A participant can stop logging or sharing without losing ordinary app access. Use their separately held study code to remove identifiable/pseudonymous packages and derived per-person rows, caches and backups under the agreed institutional process. Recompute aggregates/calibration or document already irreversibly anonymised/published outputs that cannot be withdrawn. Delete the linkage table at its agreed end. Document breach response and custodian/study-owner escalation. Local app delete/export operations do not by themselves delete coordinator copies.

## Observation checklist for the later UI review

Observe onboarding completion, confusion between facts and estimates, photo retakes, correction/reset use, missing-versus-rest understanding, nutrition completeness, hypothesis assumptions and range interpretation, finding CheckIn at the target date, recognising old-versus-current revisions, backup/restore, opt-in export and deletion. Measure completion time, errors, abandonment and explanations in participants' own words. Test real low-end phones, memory pressure, offline recovery and permission failures. Viewport emulation is not phone performance evidence. Redesign is outside this R&D package.
