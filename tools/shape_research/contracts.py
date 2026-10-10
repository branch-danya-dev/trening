from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import date
import hashlib
import json
from pathlib import Path
import re


def canonical(value) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()


def digest(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def read_json(path: Path, maximum=64_000_000):
    if path.stat().st_size > maximum:
        raise ValueError("Input exceeds size limit")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def contained(root: Path, relative: str) -> Path:
    root = root.resolve(strict=True)
    raw = root / relative
    if Path(relative).is_absolute() or any(p == ".." for p in Path(relative).parts):
        raise ValueError("Expected a relative path within the research root")
    for p in [raw, *raw.parents]:
        if p == root:
            break
        if linked(p):
            raise ValueError("Symlinks are not research inputs")
    path = raw.resolve(strict=True)
    if not path.is_relative_to(root) or not path.is_file():
        raise ValueError("Input escapes research root")
    return path


def linked(path: Path):
    return path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction())


@dataclass(frozen=True)
class Composition:
    weight_kg: float
    fat_kg: float
    fat_free_kg: float
    method: str
    regional_kg: dict[str, float] | None = None

    def validate(self):
        import math
        values = [self.weight_kg, self.fat_kg, self.fat_free_kg, *(self.regional_kg or {}).values()]
        if not all(math.isfinite(v) and v >= 0 for v in values):
            raise ValueError("Invalid composition")
        if not 20 <= self.weight_kg <= 400 or not self.method:
            raise ValueError("Unsupported weight or missing compartment method")
        if abs(self.weight_kg - self.fat_kg - self.fat_free_kg) > .25:
            raise ValueError("Composition must use FFM including bone, not lean soft tissue alone")


@dataclass(frozen=True)
class LongitudinalBodyPair:
    participant_id: str
    t0: str
    t1: str
    horizon_days: int
    sex: str
    age_t0: float
    height_m: float
    composition_t0: Composition
    composition_t1: Composition
    mesh_t0: str
    mesh_t1: str
    source_dataset: str
    source_version: str
    mesh_sha256_t0: str
    mesh_sha256_t1: str
    intervention: str | None = None
    strength: bool | None = None
    exclusion_flags: tuple[str, ...] = ()
    synthetic: bool = False

    @property
    def delta_fat(self):
        return self.composition_t1.fat_kg - self.composition_t0.fat_kg

    @property
    def delta_lean(self):
        return self.composition_t1.fat_free_kg - self.composition_t0.fat_free_kg

    def validate(self):
        if not re.fullmatch(r"[A-Za-z0-9_-]{8,80}", self.participant_id):
            raise ValueError("Use a project pseudonym, not an identifier or filename")
        if (date.fromisoformat(self.t1) - date.fromisoformat(self.t0)).days != self.horizon_days or not 1 <= self.horizon_days <= 15000:
            raise ValueError("Visit dates and horizon disagree")
        if self.sex not in ("Male", "Female", "Unknown") or not 18 <= self.age_t0 <= 100 or not 1 <= self.height_m <= 2.5:
            raise ValueError("Unsupported demographic metadata")
        for c in (self.composition_t0, self.composition_t1):
            c.validate()
        for h in (self.mesh_sha256_t0, self.mesh_sha256_t1):
            if not re.fullmatch(r"[0-9a-fA-F]{64}", h):
                raise ValueError("Missing source mesh hash")
        if not self.source_dataset or not self.source_version or not self.mesh_t0 or not self.mesh_t1:
            raise ValueError("Missing dataset provenance")

    @classmethod
    def from_dict(cls, row):
        row = dict(row)
        for k in ("composition_t0", "composition_t1"):
            row[k] = Composition(**row[k])
        pair = cls(**row)
        pair.validate()
        return pair


def split_participants(pairs, seed: str):
    if not seed:
        raise ValueError("Explicit reproducibility seed required")
    participants = sorted({p.participant_id for p in pairs}, key=lambda p: digest([seed, p]))
    if len(participants) < 5:
        raise ValueError("At least five participants required for three nonempty partitions")
    train_end = max(1, int(.6 * len(participants)))
    val_end = max(train_end + 1, int(.8 * len(participants)))
    rows = [{"participant": p, "split": "train" if i < train_end else "val" if i < val_end else "test"} for i, p in enumerate(participants)]
    body = {"version": "participant-split-1", "seed": seed, "assignments": sorted(rows, key=lambda r: r["participant"])}
    return {**body, "sha256": digest(body)}


def check_split(manifest, pairs):
    body = {k: v for k, v in manifest.items() if k != "sha256"}
    if digest(body) != manifest.get("sha256") or body.get("version") != "participant-split-1":
        raise ValueError("Split manifest changed")
    rows = manifest["assignments"]
    ids = [r["participant"] for r in rows]
    if len(ids) != len(set(ids)):
        raise ValueError("Participant leakage: duplicate/cross-split assignment")
    if set(ids) != {p.participant_id for p in pairs} or set(r["split"] for r in rows) != {"train", "val", "test"}:
        raise ValueError("Incomplete or invalid participant partition")
    return {r["participant"]: r["split"] for r in rows}


def require_data_access(gate, root: Path, pairs, manifest, repo_root: Path):
    """Operator attestations need private signed evidence; this is not legal adjudication."""
    root = root.resolve(strict=True)
    repo_root = repo_root.resolve(strict=True)
    if root.is_relative_to(repo_root) or repo_root.is_relative_to(root):
        raise ValueError("Research data root must be separate from the repository")
    required = ["dataset_obtained", "analysis_permitted", "derivative_rights_understood", "secure_storage_approved", "pseudonymized", "ethics_determination_recorded"]
    if gate.get("version") != "data-access-gate-1" or any(gate.get(k) is not True for k in required):
        raise ValueError("BLOCKED_PENDING_DATA_ACCESS")
    for k in ("approval_reference", "steward_reference", "storage_country", "derivative_rights_decision"):
        if not isinstance(gate.get(k), str) or not gate[k].strip() or "TODO" in gate[k]:
            raise ValueError("BLOCKED_PENDING_DATA_ACCESS: missing private approval evidence")
    if date.fromisoformat(gate["retention_until"]) < date.today() or gate.get("split_sha256") != manifest["sha256"]:
        raise ValueError("Expired permission or wrong split")
    check_split(manifest, pairs)
    for p in pairs:
        p.validate()
        if p.synthetic:
            raise ValueError("Synthetic pairs cannot enter real training/evaluation")
        if [p.source_dataset, p.source_version] not in gate.get("approved_releases", []):
            raise ValueError("Unapproved dataset release")
        for name, expected in [(p.mesh_t0, p.mesh_sha256_t0), (p.mesh_t1, p.mesh_sha256_t1)]:
            path = contained(root, name)
            if path.stat().st_size > 256_000_000 or hashlib.sha256(path.read_bytes()).hexdigest() != expected.lower():
                raise ValueError("Source inventory mismatch")


def pair_inventory(pairs):
    return digest([asdict(p) for p in sorted(pairs, key=lambda p: (p.participant_id, p.t0, p.t1))])


def restricted_output(path: Path, root: Path, repo_root: Path):
    """Derivative artifacts stay in the approved data root, never in Git or a public report."""
    root, repo_root = root.resolve(strict=True), repo_root.resolve(strict=True)
    if root.is_relative_to(repo_root) or repo_root.is_relative_to(root):
        raise ValueError("Restricted output root must be separate from repository")
    parent = path.parent.resolve(strict=True)
    target = path.resolve()
    if not parent.is_relative_to(root) or not target.is_relative_to(root) or target.is_relative_to(repo_root) or linked(path):
        raise ValueError("Restricted output must stay inside approved data root")
    if target.exists():
        raise ValueError("Preserve existing research artifacts; choose a new output")
    return target
