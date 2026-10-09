# Reference manifest: Hall 2011 appendix

- Version: `hall-2011-appendix-rk4-1`, independently written equation implementation, not official NIDDK software.
- Source: [Hall et al., Lancet 2011](https://pubmed.ncbi.nlm.nih.gov/21872751/), DOI 10.1016/S0140-6736(11)60812-X.
- [Official appendix](https://www.niddk.nih.gov/-/media/Files/BWP/Hall_Lancet_Web_Appendix.pdf), retrieved 2026-10-09, SHA-256 `4795C9897DA622C3561738D7391C177DF18CEF40C297D7EFCEB5A0CB9EFD87AA`.
- [NIDDK source page](https://www.niddk.nih.gov/research-funding/at-niddk/labs-branches/laboratory-biological-modeling/integrative-physiology-section/research/body-weight-planner).
- Source/license decision: equations implemented independently; no official code, PDF, figures, logos, datasets or weights redistributed. [NIDDK copyright policy](https://www.niddk.nih.gov/copyright) permits most site content with exceptions for third-party material; no blanket license for the Lancet PDF is asserted. Repository code follows this repository's terms; no NIDDK endorsement or certification. The reference stays in tools and never becomes a network/runtime dependency.

## Numerical contract

Equations 1–3 and 5–9, with known initial fat (no equation 4 estimator). Convert published kJ/MJ to kcal with 4.184 kJ/kcal. Integrate F, L, G, ECF change and AT by classical RK4, 8 steps/day. Initial G=0.5 kg; bound-water ratio=2.7. Baseline ECF is a constant offset absorbed into the initial FFM/K anchor, so only its change is reported. Lean output is baseline FFM plus tissue change, excluding subsequent glycogen/water changes. Primary run disables equation 2; secondary run includes it. No strength partition adjustment, clinical floor or macro TEF is inserted into the oracle. Added activity is a boundary input (active kcal/day from the existing exercise calculator); physical activity cost then scales with weight under equation 8.

No web calculator scraping. Source discovery is manual/offline; CI only runs committed equations. Pinned checks include equilibrium, analytic AT, analytic glycogen/ECF and RK4 step refinement. Broad catastrophic-divergence gates are not a physiological accuracy claim.

## Scenarios v1

4096 deterministic LCG samples (uint32 `1664525*x+1013904223`, seed 20261009), both sexes, age 18–75, height 150–195 cm, BMI 20–38. BF sampling uses a documented heuristic `1.2*BMI+.23*age-sexOffset+uniform(-2,2)` clipped to male 16–42%, female 25–50%; it supplies synthetic initial conditions, not a user estimator. PAL 1.3–1.8. Planned energy offset -750/-500/-250/0/+250/+500 kcal/day, bounded intake at 1500 male/1200 female. Bounds define this benchmark, not clinical prescriptions. Walking 0 or 4×45 min/week at 5.5 km/h, 4% grade; strength 0 or 3×1 h/week. Cases include full macros, carbs only and unknown macros; carbs 15/35/50/65% of intake, full-macro protein 20%, fat remainder. Initial carbs assumed 50% of baseline intake. Optional sodium 1500–4500 mg/day relative to research baseline 3000. Missing carbs in the oracle assumes 50% of planned intake (an explicit reference assumption, not a fact).

Horizons 1/2/4/8/12/24 weeks. Report signed bias, MAE, P95, max for weight (= delta-weight error), fat, lean, glycogen+water, ECF-inclusive weight and last-week weight-change error. Also save mean predicted absolute/delta weight. Strata: sex, BMI, energy band, strength, known carbs, high activity, and horizon category. No outcome-based exclusions. Reference fat-floor crossings are counted. Strength/surplus disagreements are expected because the reference has no hypertrophy model. Agreement does not establish prospective accuracy.

## Sodium prototype

`HallReference.SodiumOverlay` solves equation 2 analytically and changes scale water only; it never feeds tissue physics. Research tests cover equal sodium, increase/decrease, carb interaction and saturation. **NO-GO production:** habitual sodium and hydration are unobserved; a plausible equation and synthetic agreement alone cannot show user benefit. Runtime accepts optional sodium metadata but does not act on it or expose a sodium editor.
