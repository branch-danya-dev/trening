from dataclasses import dataclass
from typing import Protocol
import numpy as np
from .contracts import digest


def train_members(manifest, participants):
    body = {k: v for k, v in manifest.items() if k != "sha256"}
    rows = manifest["assignments"]
    ids = [r["participant"] for r in rows]
    if digest(body) != manifest["sha256"] or len(ids) != len(set(ids)):
        raise ValueError("Invalid/leaking split")
    lookup = {r["participant"]: r["split"] for r in rows}
    if not participants or any(lookup.get(p) != "train" for p in participants):
        raise ValueError("Training transform attempted to consume VAL/TEST/unknown participant")


@dataclass
class PCA:
    mean: np.ndarray
    components: np.ndarray
    eigenvalues: np.ndarray
    total_variance: float
    topology_hash: str
    split_hash: str
    training_participants: list[str]

    @classmethod
    def fit(cls, shapes, participants, manifest, dimensions, topology_hash):
        train_members(manifest, participants)
        x = np.asarray(shapes, float)
        if x.ndim != 2 or len(x) != len(participants) or not np.isfinite(x).all() or not 1 <= dimensions <= min(x.shape[1], len(x)-1):
            raise ValueError("Invalid PCA training matrix/dimension")
        mean = x.mean(axis=0)
        _, singular, components = np.linalg.svd(x-mean, full_matrices=False)
        eigenvalues = singular**2/(len(x)-1)
        if eigenvalues[0] <= 1e-18 or eigenvalues[dimensions-1] <= eigenvalues[0]*1e-12:
            raise ValueError("Requested dimension exceeds supported numerical rank")
        components = components[:dimensions].copy()
        for row in components:
            if row[np.argmax(np.abs(row))] < 0:
                row *= -1
        return cls(mean, components, eigenvalues[:dimensions], float(eigenvalues.sum()), topology_hash, manifest["sha256"], sorted(set(participants)))

    def encode(self, shapes):
        x = np.asarray(shapes, float)
        if x.shape[-1] != len(self.mean) or not np.isfinite(x).all():
            raise ValueError("PCA input dimensions/nonfinite")
        return (x-self.mean) @ self.components.T

    def decode(self, latent):
        z = np.asarray(latent, float)
        if z.shape[-1] != len(self.components) or not np.isfinite(z).all():
            raise ValueError("PCA latent dimensions/nonfinite")
        return z @ self.components + self.mean

    def reconstruction_curve(self, shapes):
        x = np.asarray(shapes, float)
        z = self.encode(x)
        return [{"dimensions": k, "explained_variance": float(self.eigenvalues[:k].sum()/self.total_variance),
                 "coordinate_rmse_m": float(np.sqrt(np.mean((x-(z[:, :k] @ self.components[:k]+self.mean))**2)))}
                for k in range(1, len(self.components)+1)]

    def artifact(self):
        value = {"version": "pca-shape-1", "numpy_version": np.__version__, "mean": self.mean.tolist(), "components": self.components.tolist(),
                 "eigenvalues": self.eigenvalues.tolist(), "total_variance": self.total_variance, "topology_hash": self.topology_hash,
                 "split_hash": self.split_hash, "training_participants": self.training_participants, "production_approved": False}
        return {**value, "sha256": digest(value)}

    @classmethod
    def restore(cls, artifact):
        body = {k: v for k, v in artifact.items() if k != "sha256"}
        if artifact["sha256"] != digest(body) or body["version"] != "pca-shape-1" or body["production_approved"] is not False:
            raise ValueError("Untrusted PCA artifact")
        mean, components, eigen = [np.asarray(body[k], float) for k in ("mean", "components", "eigenvalues")]
        if mean.ndim != 1 or components.shape != (len(eigen), len(mean)) or not np.isfinite(mean).all() or not np.isfinite(components).all() or not (eigen > 0).all():
            raise ValueError("Invalid PCA artifact arrays")
        if not np.allclose(components @ components.T, np.eye(len(eigen)), atol=1e-8) or not np.isfinite(body["total_variance"]) or body["total_variance"] < eigen.sum()-1e-10:
            raise ValueError("Invalid PCA variance/orthogonality")
        return cls(mean, components, eigen, body["total_variance"], body["topology_hash"], body["split_hash"], body["training_participants"])


def representation_benchmark(train, validation, manifest, dimensions, topology_hash):
    """Records curves only: no TEST inputs and no automatic promotion of a model."""
    lookup = {r["participant"]: r["split"] for r in manifest["assignments"]}
    if any(lookup.get(r["participant"]) != "val" for r in validation):
        raise ValueError("Representation selection must use VAL, not TEST")
    reports = {}
    for group in ["shared", "Male", "Female"]:
        tr = [r for r in train if group == "shared" or r["sex"] == group]
        va = [r for r in validation if group == "shared" or r["sex"] == group]
        if len(tr) <= dimensions or not va:
            reports[group] = {"status": "INSUFFICIENT_SUPPORT", "train": len(tr), "val": len(va)}
            continue
        try:
            pca = PCA.fit([r["shape"] for r in tr], [r["participant"] for r in tr], manifest, dimensions, topology_hash)
            reports[group] = {"train": pca.reconstruction_curve([r["shape"] for r in tr]), "val": pca.reconstruction_curve([r["shape"] for r in va]), "artifact_hash": pca.artifact()["sha256"]}
        except ValueError as e:
            reports[group] = {"status": "INSUFFICIENT_RANK", "reason": str(e)}
    return reports


@dataclass(frozen=True)
class DeltaShapeInput:
    start_latent: tuple[float, ...]
    sex: str
    age: float
    height_m: float
    start_weight_kg: float
    start_fat_kg: float | None
    delta_fat_kg: float
    delta_lean_kg: float
    horizon_days: int
    regional_muscle_delta_kg: dict[str, float] | None = None
    intervention: str | None = None


class IDeltaShapeModel(Protocol):
    version: str

    def predict(self, value: DeltaShapeInput) -> dict: ...


class ZeroChangeModel:
    version = "zero-latent-change-research-1"

    def predict(self, value: DeltaShapeInput):
        z = np.asarray(value.start_latent, float)
        numeric = [value.age, value.height_m, value.start_weight_kg, value.delta_fat_kg, value.delta_lean_kg, value.horizon_days]
        if z.ndim != 1 or len(z) == 0 or not np.isfinite(z).all() or not np.isfinite(numeric).all() or value.horizon_days <= 0:
            raise ValueError("Invalid delta-shape input")
        return {"future_latent": z.tolist(), "delta_latent": np.zeros_like(z).tolist(), "version": self.version,
                "uncertainty": "UNVALIDATED", "fallback": True, "reason": "Research baseline; no learned human change model"}


@dataclass
class Support:
    ranges: dict
    subgroups: dict
    latent_limit: float
    residual_limit: float

    @classmethod
    def fit(cls, pca, shapes, inputs, participants, manifest):
        train_members(manifest, participants)
        if len(shapes) != len(inputs) or len(inputs) != len(participants) or not inputs:
            raise ValueError("Misaligned support inputs")
        keys = sorted(inputs[0]["numeric"])
        if any(sorted(r["numeric"]) != keys for r in inputs):
            raise ValueError("Input feature schema mismatch")
        values = np.asarray([[r["numeric"][k] for k in keys] for r in inputs], float)
        if not np.isfinite(values).all():
            raise ValueError("Missing/nonfinite support features need an explicit model policy")
        ranges = {k: [float(values[:, i].min()), float(values[:, i].max())] for i, k in enumerate(keys)}
        groups = {g: len({p for p, r in zip(participants, inputs) if r["subgroup"] == g}) for g in {r["subgroup"] for r in inputs}}
        scores = np.sum(pca.encode(shapes)**2 / pca.eigenvalues, axis=1)
        residual = np.sqrt(np.mean((np.asarray(shapes)-pca.decode(pca.encode(shapes)))**2, axis=1))
        return cls(ranges, groups, max(1.0, float(scores.max())*1.5), max(.0001, float(residual.max())*1.5))

    def assess(self, pca, shape, numeric, subgroup, minimum_participants=5):
        reasons = []
        if set(numeric) != set(self.ranges):
            reasons.append("input-schema")
        for k, (low, high) in self.ranges.items():
            v = numeric.get(k)
            if v is None or not np.isfinite(v) or not low <= v <= high:
                reasons.append("range:"+k)
        z = pca.encode(shape)
        score = float(np.sum(z**2/pca.eigenvalues))
        residual = float(np.sqrt(np.mean((np.asarray(shape)-pca.decode(z))**2)))
        if score > self.latent_limit:
            reasons.append("latent-distance")
        if residual > self.residual_limit:
            reasons.append("pca-residual")
        if self.subgroups.get(subgroup, 0) < minimum_participants:
            reasons.append("subgroup-support")
        return {"score_distance_squared": score, "residual_m": residual, "is_probability": False, "fallback": bool(reasons), "reasons": reasons,
                "range_multiplier": 2.0 if reasons else 1.0, "policy": "train-support-heuristic-1"}
