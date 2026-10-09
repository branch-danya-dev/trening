from pathlib import Path
from .contracts import digest, read_json, contained, require_data_access, check_split, pair_inventory
from .geometry import Mesh, correspondence
from .metrics import compare, aggregate, strata


def freeze_evaluation(manifest, pairs, model_artifact, policy):
    """Call before TEST is opened. Receipt is append-only at the operator's secure boundary."""
    assignments = check_split(manifest, pairs)
    if not model_artifact.get("sha256") or model_artifact["sha256"] != digest({k: v for k, v in model_artifact.items() if k != "sha256"}):
        raise ValueError("Unfrozen model artifact")
    if model_artifact.get("split_hash") != manifest["sha256"] or any(assignments.get(p) != "train" for p in model_artifact.get("training_participants", [])):
        raise ValueError("Model provenance crosses TRAIN boundary")
    if not model_artifact.get("training_participants"):
        raise ValueError("Model training provenance is missing")
    for key in ["surface_improvement_fraction", "girth_improvement_fraction", "maximum_subgroup_regression_fraction", "minimum_test_participants"]:
        if key not in policy:
            raise ValueError("Preregister GO/subgroup/sample policy before TEST")
    if not 0 < policy["surface_improvement_fraction"] < 1 or not 0 < policy["girth_improvement_fraction"] < 1 or policy["minimum_test_participants"] < 5 or not 0 <= policy["maximum_subgroup_regression_fraction"] < 1:
        raise ValueError("Invalid preregistered thresholds")
    value = {"version": "evaluation-freeze-1", "split_hash": manifest["sha256"], "inventory_hash": pair_inventory(pairs),
             "model_hash": model_artifact["sha256"], "policy": policy}
    return {**value, "sha256": digest(value)}


def evaluate_registered(*, pairs, manifest, frozen, model_artifact, policy, gate, data_root: Path, repo_root: Path, cases, synthetic_test=False):
    """Prediction inputs remain restricted. No remote provider or production integration."""
    if synthetic_test:
        if not pairs or any(not p.synthetic for p in pairs):
            raise ValueError("Test mode accepts only explicitly synthetic pairs")
    else:
        require_data_access(gate, data_root, pairs, manifest, repo_root)
    expected = freeze_evaluation(manifest, pairs, model_artifact, policy)
    if frozen != expected:
        raise ValueError("Model/policy/inventory changed after evaluation freeze")
    assignments = check_split(manifest, pairs)
    held_out = [p for p in pairs if assignments[p.participant_id] == "test" and not p.exclusion_flags]
    expected_keys = {digest([p.participant_id, p.t0, p.t1]) for p in held_out}
    if len(cases) != len(expected_keys) or set(cases) != expected_keys:
        raise ValueError("Missing, duplicate or extra held-out predictions")
    results = {"procedural": [], "candidate": []}
    for pair in held_out:
        case = cases[digest([pair.participant_id, pair.t0, pair.t1])]
        if case.get("t0_source_sha256") != pair.mesh_sha256_t0 or case.get("t1_source_sha256") != pair.mesh_sha256_t1:
            raise ValueError("Registered prediction/source inventory mismatch")
        reference = Mesh(**case["actual"])
        candidate = Mesh(**case["candidate"])
        baseline = case["procedural"]
        if baseline.get("version") != "procedural-research-bridge-1" or baseline.get("horizonDays") != pair.horizon_days or baseline.get("shapeVersion") != "body-shape-procedural-1":
            raise ValueError("Mandatory production baseline metadata/horizon missing")
        if baseline.get("synthetic") != synthetic_test or case.get("model_hash") != frozen["model_hash"]:
            raise ValueError("Prediction provenance mismatch")
        if baseline.get("informationPolicy") != policy.get("information_policy"):
            raise ValueError("Unequal input-information budget")
        procedural = correspondence(Mesh(**baseline["endpoint"]), reference, case["makehuman_to_research"])
        options = {k: case[k] for k in ("rings", "regions") if k in case}
        for name, mesh in [("procedural", procedural), ("candidate", candidate)]:
            metrics = compare(mesh, reference, **options, fat_kg=pair.composition_t1.fat_kg, ffm_kg=pair.composition_t1.fat_free_kg)
            results[name].append({"participant": pair.participant_id, "strata": strata(pair), "metrics": metrics,
                                  "fallback": case.get("fallback") if name == "candidate" else False})
    reports = {k: aggregate(v, minimum_participants=policy["minimum_test_participants"]) for k, v in results.items()}
    comparisons = {}
    for group, baseline_group in reports["procedural"].items():
        candidate_group = reports["candidate"][group]
        if "metrics" not in baseline_group or "metrics" not in candidate_group:
            comparisons[group] = {"status": "INSUFFICIENT_SUPPORT"}; continue
        comparisons[group] = {}
        for metric in ["surface_mean_m", "girth_mae_cm"]:
            b = baseline_group["metrics"].get(metric, {}).get("participant_mean")
            c = candidate_group["metrics"].get(metric, {}).get("participant_mean")
            threshold = policy["surface_improvement_fraction" if metric == "surface_mean_m" else "girth_improvement_fraction"]
            comparisons[group][metric] = {"baseline": b, "candidate": c,
                "relative_improvement": (b-c)/b if b is not None and b > 0 and c is not None else None,
                "material_improvement": b is not None and b > 0 and c is not None and c <= b*(1-threshold),
                "catastrophic_regression": b is not None and c is not None and c > b*(1+policy["maximum_subgroup_regression_fraction"])}
    return {"version": "held-out-comparison-1", "evidence": "SYNTHETIC_TEST_ONLY" if synthetic_test else "RESTRICTED_HELD_OUT_REQUIRES_INDEPENDENT_REVIEW",
            "production_go": False, "freeze_sha256": frozen["sha256"], "excluded_pairs": sum(bool(p.exclusion_flags) for p in pairs),
            "reports": reports, "paired_comparisons": comparisons,
            "decision": "No automatic GO. Review paired improvement, subgroup catastrophes, support, rights and output constraints."}


def load_cases(root: Path, case_files):
    return {key: read_json(contained(root, name)) for key, name in case_files.items()}
