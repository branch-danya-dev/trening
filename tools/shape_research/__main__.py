import argparse
import json
import hashlib
from pathlib import Path
from .contracts import LongitudinalBodyPair, read_json, canonical, split_participants, require_data_access, contained
from .geometry import import_mesh, rigid_landmark_registration, correspondence, delete_cache
from .harness import evaluate_registered, load_cases
from .models import representation_benchmark


def gated_context(c):
    pairs = [LongitudinalBodyPair.from_dict(r) for r in read_json(Path(c["pairs"]))]
    manifest, gate = read_json(Path(c["split"])), read_json(Path(c["gate"]))
    root, repo = Path(c["data_root"]), Path(c["repo_root"])
    require_data_access(gate, root, pairs, manifest, repo)
    return pairs, manifest, gate, root, repo


def main():
    parser = argparse.ArgumentParser(description="Offline C0 infrastructure; no production training or claims")
    commands = parser.add_subparsers(dest="command", required=True)
    split = commands.add_parser("split")
    split.add_argument("pairs", type=Path); split.add_argument("output", type=Path); split.add_argument("--seed", required=True)
    register = commands.add_parser("register")
    register.add_argument("config", type=Path); register.add_argument("output", type=Path)
    evaluate = commands.add_parser("evaluate")
    evaluate.add_argument("config", type=Path); evaluate.add_argument("output", type=Path)
    pca = commands.add_parser("pca-report")
    pca.add_argument("config", type=Path); pca.add_argument("output", type=Path)
    delete = commands.add_parser("delete-cache")
    delete.add_argument("cache", type=Path); delete.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    if args.command == "split":
        result = split_participants([LongitudinalBodyPair.from_dict(r) for r in read_json(args.pairs)], args.seed)
    elif args.command == "register":
        c = read_json(args.config)
        _, _, _, root, _ = gated_context(c)
        source = import_mesh(contained(root, c["source"]), c["unit"], c["orientation"])
        template = import_mesh(contained(root, c["template"]))
        mapping = read_json(contained(root, c["mapping"]))
        aligned, metadata = rigid_landmark_registration(source, c["source_landmarks"], c["target_landmarks"], height_scale=c.get("height_scale", 1), max_rms_m=c.get("max_rms_m", .025))
        result = {"mesh": correspondence(aligned, template, mapping).to_dict(), "registration": metadata, "mapping_sha256": mapping["sha256"],
                  "source_file_sha256": hashlib.sha256(contained(root, c["source"]).read_bytes()).hexdigest()}
    elif args.command == "pca-report":
        c = read_json(args.config)
        _, manifest, _, root, _ = gated_context(c)
        rows = read_json(contained(root, c["registered_shapes"]))
        lookup = {r["participant"]: r["split"] for r in manifest["assignments"]}
        # Reject TEST in the selection file instead of silently accepting leaked preprocessing.
        if any(lookup.get(r["participant"]) not in {"train", "val"} for r in rows):
            raise ValueError("PCA selection input must contain TRAIN/VAL only")
        result = representation_benchmark([r for r in rows if lookup[r["participant"]] == "train"], [r for r in rows if lookup[r["participant"]] == "val"], manifest, c["dimensions"], c["topology_hash"])
    elif args.command == "evaluate":
        c = read_json(args.config)
        pairs, manifest, gate, root, repo = gated_context(c)
        # No synthetic override on the real-data CLI. Test-only API is exercised by unit tests.
        result = evaluate_registered(pairs=pairs, manifest=manifest, frozen=read_json(Path(c["freeze"])), model_artifact=read_json(Path(c["model"])),
                                     policy=c["policy"], gate=gate, data_root=root, repo_root=repo, cases=load_cases(root, c["cases"]))
    else:
        print(json.dumps(delete_cache(args.cache, args.execute)))
        return
    args.output.write_bytes(canonical(result))
    print("Restricted research artifact written; no production promotion.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError) as error:
        raise SystemExit(str(error))
