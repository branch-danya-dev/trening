# Shape Up! request dossier

Prepared 2026-10-09. **NOT SUBMITTED; BLOCKED_PENDING_DATA_ACCESS.** No affiliation, ethics determination or contractual permission is asserted by this draft.

User has explicitly prohibited submission at this stage. Fields to complete later: **[LEGAL ENTITY/INDEPENDENT APPLICANT STATUS] [RESPONSIBLE INVESTIGATOR] [RESEARCH EMAIL] [AFFILIATION OR APPROVED INDEPENDENT ROUTE] [ETHICS DETERMINATION] [ACCESS/STORAGE COUNTRIES] [SECURITY ENVIRONMENT] [RETENTION TERM] [SIGNATORY]**. None currently exists or has been approved; country remains undecided.

## Official route and observed blocker

Custodian contact: Shepherd Research Lab, University of Hawaiʻi Cancer Center; lead John A. Shepherd. The [researcher page](https://shepherdresearchlab.cc.hawaii.edu/shapeup/for-researchers/) names UH, Pennington, UCSF and University of Washington as partners. The [request policy](https://shepherdresearchlab.cc.hawaii.edu/request-data/) routes applications to an Analysis Plan form and lists `data@shepherdresearchlab.org`.

On 2026-10-09 the official [form](https://forms.gle/AtipfeATzXrkvJAr6) redirected to `/closedform` and visibly stated that it no longer accepts responses. No form was submitted. Request a reopened route from the listed contact after the applicant supplies their identity.

The request policy describes investigator review, monthly project updates, return of derived variables, funder acknowledgement and PI participation in manuscript authorship. Treat these as obligations to discuss before acceptance, not commitments already made by this project.

## Proposed title and summary — ready to transfer into application

**Participant-held-out prediction of longitudinal adult surface-shape change conditional on body-composition change.**

We develop a local fitness planning application with a deterministic composition forecast and procedural body geometry. We seek to test whether a transparent low-dimensional statistical model improves the prediction of future external body shape given baseline shape, elapsed time and changes in fat and lean compartments. This is engineering research, not diagnosis, a DXA substitute, or a claim of individual predictive accuracy. The intended eventual product is commercial. Please assess this purpose explicitly and distinguish research access from commercial use and distribution of derived parameters.

## Cohort and requested products

Primary request: adult same-participant baseline/follow-up 3D optical surfaces and same-visit DXA compartment values. The [2023 longitudinal paper](https://doi.org/10.1016/j.ajcnut.2023.02.006) describes 133 participants from six complementary intervention studies and follow-up of 3–23 weeks. These are not assumed to be part of a single transferable Shape Up! release. Please identify the releasable participants, versions and responsible custodians for TREAT, FB4, REALPA, LSU athletes, OPS II and bariatric cohorts; identify any additional approval requirements. We propose excluding surgery and pharmacological interventions from the primary fitness analysis, retaining only explicitly approved sensitivity analyses.

Requested minimum variables, subject to the data dictionary:

- Project-scoped pseudonymous participant key, study/site and visit code; day offsets preserving true horizon instead of birth dates or direct identifiers.
- Adult age at baseline, sex variable definition, measured height and weight at each visit.
- DXA total fat, lean soft tissue, bone mineral content and/or fat-free mass, with definitions, units, scanner, calibration and quality flags. Never silently treat lean soft tissue as FFM.
- Regional fat/lean for arms, legs and trunk if available, including region definitions and exclusions.
- Raw de-identified surface or approved registered mesh at each visit, mesh units/orientation, topology, landmarks, pose, scan/reconstruction method, quality and missing-region flags.
- Same-day duplicate scans identified as repeats, never as longitudinal outcomes.
- Intervention category, duration and strength exposure when available; acquisition dates only if relative offsets are inadequate and separately approved.
- Registration/derived mesh licenses and all preprocessing dependencies, including any Meshcapade/SMPL restrictions.

Paired surfaces are needed to learn *change* rather than between-person appearance. DXA isolates the composition input and enables consistency checks. Cross-sectional surfaces or duplicate scans alone cannot answer the primary question.

## Analysis and deliverables

Freeze participant-level TRAIN/VAL/TEST assignment, including all visits and overlapping studies. Fit registration choices, PCA and covariate preprocessing on TRAIN. Select dimension and regularization on VAL; freeze hashes, model and thresholds before one TEST evaluation. Compare shared and sex-specific PCA where sample support permits. Begin with zero-change and linear/ridge latent baselines; no unconstrained vertex regression. Run the exact procedural application baseline under the same input-information budget. Report surface, silhouettes, girths, closed regional/total volumes, support and subgroup failures. Separate conditional-on-observed-composition evaluation from end-to-end forecasts; observed T1 composition is not available to a prospective forecast.

Publish negative results, exclusions, reconstruction curves, sample counts and limitations. Return approved derived variables and analysis code through the custodian's approved channel. No participant surface, face, identifier, embedding or per-person result will be placed in a public repository. Publication, acknowledgement, pre-release review and authorship arrangements remain subject to agreement.

## Security, retention and applicant checklist

Use [DATA_GOVERNANCE](DATA_GOVERNANCE.md). Prefer custodian-hosted access. If local access is permitted, use an approved encrypted restricted workstation with named users, MFA and controlled export review. No raw data in Git, consumer sync, application assets, third-party AI prompts or remote model APIs. Retention starts only after a written schedule is agreed; request the permitted duration, backup handling, revocation and deletion evidence requirements. No re-identification, linkage or onward transfer without explicit permission.

Before submission the user/organisation must supply: legal entity and address; PI/responsible investigator and professional email; research affiliation or partner; ethics approval/exemption determination; countries of access/storage; approved security environment; funding and publication budget; authorised signatory. None are inferred from the developer account or locale.

## Questions requiring written answers

1. Can this commercial entity apply directly, or is a bona fide academic/research collaborator mandatory? What IRB/ethics determination is required for secondary use?
2. Which longitudinal cohorts and surfaces are releasable, under which dataset version and study-specific agreements?
3. Is derivative model training permitted? May PCA bases, latent regressors and learned weights be commercially used, redistributed and embedded in a local app? Are weights subject to disclosure review or withdrawal?
4. Are commercial research, product deployment and model distribution separate permissions or fees?
5. What attribution, manuscript approval periods, PI authorship, progress reporting and return-of-derived-data terms apply?
6. What access countries, remote access, cross-border transfer, storage, retention, backups and deletion rules apply?
7. May registration outputs be reused and distributed independently of scanner/mesh-registration software licenses?
8. What participant overlap must be reconciled across source studies and pretraining datasets?

Until answered and data obtained, C1 remains blocked. A public paper is not a data license.
