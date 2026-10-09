# Fenland request dossier

Prepared 2026-10-09. **NOT SUBMITTED; BLOCKED_PENDING_DATA_ACCESS.** Applicant identity and organisational approvals are missing.

User has explicitly prohibited submission at this stage. Fields to complete later: **[LEGAL ENTITY/INDEPENDENT APPLICANT STATUS] [RESPONSIBLE INVESTIGATOR] [RESEARCH EMAIL] [AFFILIATION OR APPROVED COMMERCIAL ROUTE] [ETHICS DETERMINATION] [ACCESS/STORAGE COUNTRIES] [SRCP/APPROVED ENVIRONMENT] [RETENTION TERM] [FUNDING] [SIGNATORY]**. No institution, researcher, IRB or partner is asserted.

## Official route, availability and licensing audit

Owner/access authority: IMS Epidemiology, University of Cambridge, with Fenland Study Management Committee approval. The [current study page](https://studies.epi.ims.cam.ac.uk/fenland/data-sharing) links the DOCX request form, dictionary and policy; it lists phase 3 DEXA and anthropometry as available, but warns that the online dictionary is incomplete. It still lists `datasharing@mrc-epid.cam.ac.uk`; [policy v3.1, April 2026](https://studies.epi.ims.cam.ac.uk/files/data-sharing/data-access-sharing-policy-v3.1.pdf) lists `datasharing@ims.cam.ac.uk`. Use the current policy contact and ask the team to confirm routing.

Policy v3.1 requires bona fide research and considers commercial use case by case with Cambridge Enterprise. IP terms are agreement-specific. Most access uses Cambridge SRCP; approved summary outputs/code can leave, individual records cannot. Alternative secure transfers require an institutional agreement. No re-identification or unapproved linkage. Exact geographic access, retention, weight export and commercial rights remain unanswered. Research access does not establish deployment permission.

The [Fenland methods publication](https://publications.mrc-epid.cam.ac.uk/publication/6030) fits meshes to DXA silhouettes plus anthropometry. Such derived geometry is not independent optical surface ground truth; label provenance and analyse separately. Availability of actual repeated 3DO surface scans has **not been established**.

## Proposed title and application text

**Independent evaluation of constrained longitudinal adult body-shape forecasts and regional composition consistency.**

We propose an openly reported engineering comparison of a transparent latent-change model against an existing procedural fitness forecast. Our intended eventual application is a commercial local fitness planning product. Please determine eligibility and any Cambridge Enterprise pathway before granting access. We do not request preferential/exclusive access and do not represent this work as non-commercial.

The research question is whether baseline surface shape, demographic covariates, elapsed time and composition deltas predict follow-up surface shape better than the current procedural baseline. We first need confirmation that suitable independent surface outcomes exist. If only DXA-derived meshes exist, the project must be narrowed to an explicitly labelled auxiliary/long-horizon study; those meshes cannot validate prediction of independently measured optical surfaces, and DXA-derived inputs/outcomes require leakage analysis.

## Variables and scope

Please provide exact dictionary variable names and release version for linked phases 1/2/3: project pseudonym, visit ordering/relative day offsets, adult age, sex definition, measured height/weight, waist/hip/other measured girths, DXA total/regional fat/lean/BMC and compartment definitions, quality/scanner/analysis versions, and activity/strength metadata where available. Request actual external-body scans with units/pose/topology/landmarks/acquisition quality **only if collected and releasable**. Identify derived meshes separately with reconstruction algorithm, source inputs and their license.

Use the minimum adult subset with paired visits and adequate quality. Exact birth dates, addresses, contacts, NHS IDs, face textures, geolocation, genetics and linked clinical records are not requested. No unapproved external dataset linkage. Long intervals are reported separately from 4/8/12-week fitness horizons; no extrapolation of their accuracy to short horizons.

## Design, outcomes and outputs

All visits for a participant stay in one deterministic TRAIN/VAL/TEST partition. PCA, transforms, input scaling and support estimates use TRAIN only. Hyperparameters use VAL. Freeze model/configuration hashes and preregistered improvement and subgroup thresholds before TEST. Report participant-level surface errors, girths, silhouettes, volume/composition consistency, uncertainty-range coverage and missingness. Explicitly compare the current procedural prediction, simple composition/girth baseline and latent-change candidate under equal input access. Use observed composition only in a separately labelled conditional analysis. Publish failures and withheld/out-of-support subgroups.

No weights or embeddings leave SRCP without disclosure review and express approval. The preferred deliverables are approved aggregate reports, reproducible code and agreed derived variables returned to the custodian. Publication, acknowledgement, review, funding and costs will be agreed before work starts.

## Governance and submission checklist

Apply [DATA_GOVERNANCE](DATA_GOVERNANCE.md). SRCP access is the preferred plan; local storage is conditional on written authorisation, never the default assumption. No external AI services, Git, cloud sync, onward transfers or re-identification. Request retention/revocation/deletion rules, including fitted models and caches, before enabling the gate.

The DOCX form is linked on the official study page. Transfer this title, rationale, variable request and design into it once a real applicant provides legal entity, investigator, affiliation, professional contact, collaborators, ethics determination, funding, country and security arrangements. An authorised human must make contractual declarations. The dossier is ready; neither a signed application nor an email was sent.

## Exact unanswered questions

- Can the proposed commercial entity apply directly, and which research-affiliation/ethics requirements apply?
- Does Fenland have repeat independent optical body surfaces, or only DXA-derived geometry? What raw/registered products and participant counts are available per visit?
- Which exact variables/releases and participant linkage are approved? What restrictions apply to derived meshes and registration dependencies?
- May approved data train a latent model and may its weights be exported, redistributed and used commercially? What terms, fees, attribution and publication review apply?
- Which countries and users may access SRCP? Are local encrypted copies ever allowed for this scope?
- What are the retention term, deletion verification, revocation and backup requirements? How are already published/embedded models handled after withdrawal?

No positive data/licence answer is presumed from the website.
