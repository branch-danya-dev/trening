"""Opt-in offline prospective analysis. No recruitment, upload, photos or calibration fitting."""
from collections import Counter, defaultdict
from datetime import date, datetime
import argparse
import hashlib
import html
import json
import math
from pathlib import Path
import re
import zipfile


def load_package(path):
    if path.stat().st_size > 64_000_000:
        raise ValueError("Package exceeds intake limit")
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as archive:
            entries = [i for i in archive.infolist() if i.filename == "analysis.json"]
            if len(entries) != 1 or entries[0].file_size > 8_000_000:
                raise ValueError("Missing, duplicated or oversized analysis.json")
            raw = archive.read(entries[0])
    else:
        if path.stat().st_size > 8_000_000:
            raise ValueError("Analysis JSON exceeds limit")
        raw = path.read_bytes()
    data = json.loads(raw)
    if data.get("format") != "trening-validation" or data.get("schemaVersion") != 1:
        raise ValueError("Unsupported validation package")
    return data


def finite(v):
    return isinstance(v, (float, int)) and not isinstance(v, bool) and math.isfinite(v)


def date_of(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).date()


def summarize(manifest, packages, minimum=5):
    kind = manifest.get("data_kind")
    if manifest.get("version") != "prospective-intake-1" or kind not in {"synthetic-test", "prospective-consented"}:
        raise ValueError("Explicit intake provenance required")
    entries = manifest["participants"]
    ids = [e["study_id"] for e in entries]
    if len(ids) != len(set(ids)) or any(not re.fullmatch(r"[A-Za-z0-9_-]{8,80}", p) for p in ids):
        raise ValueError("One current package per assigned pseudonym; duplicates prohibited")
    if any(e.get("consent_confirmed") is not True for e in entries):
        raise ValueError("Opt-in consent required")
    if len(packages) != len(entries):
        raise ValueError("Package/participant count mismatch")
    exclusions, coverage, selected, geometry = Counter(), [], {}, []
    hypotheses = outcomes = 0
    for entry, package in zip(entries, packages):
        if kind != "synthetic-test" and package.get("synthetic") is True:
            raise ValueError("Synthetic fixture cannot enter a real report")
        for h in package.get("hypotheses", []):
            hypotheses += 1
            outcome, origin = h.get("outcome"), h.get("origin", {})
            evidence = h.get("evidence", {})
            coverage.append({"participant": entry["study_id"], "activity": evidence.get("activity", {}).get("factualDays"),
                             "nutrition": evidence.get("nutrition", {}).get("eligibleDayCount")})
            if not outcome:
                exclusions["missing-outcome"] += 1
                continue
            outcomes += 1
            try:
                start, target, observed = [date.fromisoformat(v) for v in (h["localStartDate"], h["targetDate"], outcome["observedAt"])]
                created, cutoff, recorded = [datetime.fromisoformat(v.replace("Z", "+00:00")) for v in (origin["createdAt"], h["evidenceCutoff"], outcome["recordedAt"])]
                days = (observed-start).days
                if created.tzinfo is None or cutoff.tzinfo is None or recorded.tzinfo is None:
                    raise ValueError("Timezone required")
                prospective = origin.get("reconstructed") is False and created.date() == start and cutoff <= created < recorded and created.date() < observed
                if not prospective or target != start.fromordinal(start.toordinal()+h["horizonDays"]):
                    raise ValueError("Nonprospective")
            except (KeyError, TypeError, ValueError):
                exclusions["origin-unverified-or-retrospective"] += 1
                continue
            if observed != target or outcome.get("timing") != "Exact":
                exclusions["off-target-sensitivity-only"] += 1
                continue
            baseline = origin.get("baseline", {})
            subgroup = ["all", "sex:"+str(baseline.get("sex", "unknown"))]
            age, height, weight = [baseline.get(k) for k in ("age", "heightCm", "weightKg")]
            subgroup.append("age:"+("unknown" if not finite(age) else "18-39" if age < 40 else "40-59" if age < 60 else "60+"))
            bmi = weight/(height/100)**2 if finite(weight) and finite(height) and height > 0 else None
            subgroup.append("bmi:"+("unknown" if bmi is None else "<18.5" if bmi < 18.5 else "18.5-25" if bmi < 25 else "25-30" if bmi < 30 else ">=30"))
            for row in outcome.get("rows", []):
                metric = row.get("metric", "")
                if metric not in {"WeightKg", "BodyFatPercent", *["Girth:"+g for g in ["Chest", "Waist", "Hips", "Biceps", "Thigh", "Neck", "Calf", "Wrist"]]}:
                    exclusions["unknown-metric"] += 1; continue
                actual, predicted, quality = [row.get(k) for k in ("actual", "predicted", "sourceQuality")]
                if row.get("exclusionReason") or not all(finite(v) for v in (actual, predicted, quality)) or not 0 <= quality <= 1 or row.get("horizonDays") != days:
                    exclusions["ineligible-or-invalid-row"] += 1; continue
                endpoint = h.get("endpoint", {})
                interval = endpoint.get("weightRange") if metric == "WeightKg" else endpoint.get("girthRanges", {}).get(metric.removeprefix("Girth:"))
                covered = None
                if interval and all(finite(interval.get(k)) for k in ("lower", "upper")) and interval["lower"] <= interval["upper"]:
                    covered = interval["lower"] <= actual <= interval["upper"]
                key = (entry["study_id"], observed.isoformat(), metric)
                value = {"participant": entry["study_id"], "metric": metric, "horizon": days, "error": actual-predicted, "covered": covered,
                         "groups": subgroup, "model": h.get("modelVersion", "unknown"), "source": outcome.get("source", "unknown"), "created": created.isoformat()}
                if key in selected:
                    exclusions["same-day-metric-duplicate"] += 1
                if key not in selected or value["created"] > selected[key]["created"]:
                    selected[key] = value
            for checkin in package.get("checkIns", []):
                if checkin.get("hypothesis", {}).get("reference") != h.get("reference") or checkin.get("observedDate") != observed.isoformat():
                    continue
                for metric, actual in checkin.get("avatarGeometry", {}).get("metrics", {}).items():
                    if metric.startswith("girth."):
                        target_girth = h.get("endpoint", {}).get("girths", {}).get(metric[6:])
                        if finite(actual.get("value")) and finite(target_girth):
                            geometry.append({"participant": entry["study_id"], "metric": metric, "error": actual["value"]-target_girth})
    grouped = defaultdict(list)
    for row in selected.values():
        for group in row["groups"]:
            grouped[(group, row["metric"], row["horizon"], row["model"], row["source"])].append(row)
    summaries = []
    for (group, metric, horizon, model, source), rows in sorted(grouped.items()):
        per_person = defaultdict(list)
        for r in rows: per_person[r["participant"]].append(r)
        if len(per_person) < minimum:
            summaries.append(dict(group=group, metric=metric, horizon_days=horizon, model=model, source=source, status="SUPPRESSED_SMALL_CELL"));continue
        biases, maes, rates = [], [], []
        for person_rows in per_person.values():
            errors = [r["error"] for r in person_rows]
            biases.append(sum(errors)/len(errors));maes.append(sum(abs(e) for e in errors)/len(errors))
            present = [r["covered"] for r in person_rows if r["covered"] is not None]
            if present: rates.append(sum(present)/len(present))
        summaries.append(dict(group=group, metric=metric, horizon_days=horizon, model=model, source=source, participants=len(per_person), observations=len(rows),
                              mae=sum(maes)/len(maes), bias=sum(biases)/len(biases), range_coverage=sum(rates)/len(rates) if rates else None,
                              range_participants=len(rates), range_observations=sum(r["covered"] is not None for r in rows)))
    def coverage_mean(key):
        by_person = defaultdict(list)
        for row in coverage:
            if finite(row[key]): by_person[row["participant"]].append(row[key])
        means = [sum(v)/len(v) for v in by_person.values()]
        return {"participants": len(means), "mean_days": sum(means)/len(means) if len(means) >= minimum else None}
    geometry_report = []
    for metric in sorted({r["metric"] for r in geometry}):
        by_person = defaultdict(list)
        for r in geometry:
            if r["metric"] == metric: by_person[r["participant"]].append(abs(r["error"]))
        means = [sum(v)/len(v) for v in by_person.values()]
        geometry_report.append({"metric": metric, "mae_cm": sum(means)/len(means) if len(means) >= minimum else None, "participants": len(means)})
    return {"version": "prospective-analysis-1", "status": "SYNTHETIC_TEST_ONLY" if kind == "synthetic-test" else "NO_PROSPECTIVE_DATA" if not selected else "DESCRIPTIVE_PROSPECTIVE_REPORT",
            "participants": len(entries), "hypotheses": hypotheses, "outcomes": outcomes, "missing_outcomes": hypotheses-outcomes,
            "exclusions": dict(exclusions), "metrics": summaries, "coverage": {k: coverage_mean(k) for k in ("activity", "nutrition")},
            "avatar_girth_diagnostics": geometry_report, "geometry_limitation": "Target girths versus fitted Avatar-derived girths; no independent surface error or measured muscle mass.",
            "claims": "Descriptive errors only. Heuristic range coverage is not a confidence interval; no accuracy percentage. No calibration fitted."}


def write_report(report, output):
    output.mkdir(parents=True, exist_ok=True)
    (output/"report.json").write_text(json.dumps(report, indent=2, allow_nan=False), encoding="utf-8")
    rows = "".join("<tr>"+"".join("<td>"+html.escape(str(r.get(k, "—")))+"</td>" for k in ["group", "metric", "horizon_days", "model", "source", "participants", "mae", "bias", "range_coverage", "status"])+"</tr>" for r in report["metrics"])
    (output/"report.html").write_text('<!doctype html><meta charset="utf-8"><title>Prospective validation</title><style>body{font:16px system-ui;margin:2rem}td,th{padding:.5rem;border-bottom:1px solid #ccc}table{border-collapse:collapse}</style><h1>'+html.escape(report["status"])+"</h1><p>"+html.escape(report["claims"])+"</p><table><tr>"+"".join("<th>"+h+"</th>" for h in ["Group", "Metric", "Days", "Model", "Source", "Participants", "MAE", "Bias", "Range coverage", "Status"])+"</tr>"+rows+"</table><pre>"+html.escape(json.dumps({k:v for k,v in report.items() if k != "metrics"}, indent=2))+"</pre>", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("intake", type=Path);parser.add_argument("output", type=Path)
    args = parser.parse_args()
    manifest = json.loads(args.intake.read_text(encoding="utf-8-sig"))
    packages = []
    for entry in manifest["participants"]:
        path = (args.intake.parent/entry["path"]).resolve(strict=True)
        if hashlib.sha256(path.read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError("Intake file checksum mismatch")
        packages.append(load_package(path))
    write_report(summarize(manifest, packages), args.output)
