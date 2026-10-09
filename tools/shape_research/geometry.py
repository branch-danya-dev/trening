from dataclasses import dataclass
from pathlib import Path
import numpy as np
from .contracts import digest, read_json, linked


@dataclass
class Mesh:
    vertices: np.ndarray
    triangles: np.ndarray

    def __post_init__(self):
        self.vertices = np.asarray(self.vertices, dtype=np.float64)
        raw = np.asarray(self.triangles)
        if raw.ndim != 2 or raw.shape[1] != 3 or not np.issubdtype(raw.dtype, np.integer):
            raise ValueError("Triangulated integer topology required")
        self.triangles = raw.astype(np.int64)
        if self.vertices.ndim != 2 or self.vertices.shape[1] != 3 or not 4 <= len(self.vertices) <= 500_000:
            raise ValueError("Invalid vertex array")
        if not 1 <= len(raw) <= 1_000_000 or not np.isfinite(self.vertices).all() or raw.min() < 0 or raw.max() >= len(self.vertices):
            raise ValueError("Invalid mesh")
        if np.abs(self.vertices).max() > 1e9:
            raise ValueError("Unsafe coordinate magnitude")
        t = self.vertices[self.triangles]
        if (np.linalg.norm(np.cross(t[:, 1]-t[:, 0], t[:, 2]-t[:, 0]), axis=1) < 1e-12).any():
            raise ValueError("Degenerate triangles")

    @property
    def topology_hash(self):
        return digest({"vertices": len(self.vertices), "triangles": self.triangles.tolist()})

    def to_dict(self):
        return {"vertices": self.vertices.tolist(), "triangles": self.triangles.tolist()}


def import_mesh(path: Path, unit="m", orientation=None):
    """OBJ adapter deliberately accepts triangular faces only; no materials or external files."""
    if path.stat().st_size > 256_000_000:
        raise ValueError("Mesh exceeds import limit")
    if path.suffix.lower() == ".json":
        raw = read_json(path, 256_000_000)
        mesh = Mesh(raw["vertices"], raw["triangles"])
    elif path.suffix.lower() == ".obj":
        vertices, triangles = [], []
        for line in path.read_text(encoding="utf-8").splitlines():
            fields = line.split()
            if not fields or fields[0].startswith("#"):
                continue
            if fields[0] == "v":
                if len(fields) != 4:
                    raise ValueError("OBJ must have XYZ vertices")
                vertices.append([float(x) for x in fields[1:]])
            if fields[0] == "f":
                if len(fields) != 4:
                    raise ValueError("Triangulate explicitly before import")
                face = [int(x.split("/")[0]) for x in fields[1:]]
                triangles.append([x-1 if x > 0 else len(vertices)+x for x in face])
        mesh = Mesh(vertices, triangles)
    else:
        raise ValueError("Supported adapters: .obj and .json")
    if unit not in {"m", "cm", "mm"}:
        raise ValueError("Explicit supported length unit required")
    rotation = np.eye(3) if orientation is None else np.asarray(orientation, dtype=float)
    if rotation.shape != (3, 3) or not np.allclose(rotation.T @ rotation, np.eye(3), atol=1e-9) or not np.isclose(np.linalg.det(rotation), 1):
        raise ValueError("Orientation must be a proper rotation; reflections prohibited")
    return Mesh(mesh.vertices @ rotation.T * {"m": 1, "cm": .01, "mm": .001}[unit], mesh.triangles)


def rigid_landmark_registration(mesh, source_landmarks, target_landmarks, *, max_rms_m=.025, height_scale=1.0):
    a, b = np.asarray(source_landmarks, float), np.asarray(target_landmarks, float)
    if a.shape != b.shape or a.ndim != 2 or a.shape[1] != 3 or len(a) < 4 or not np.isfinite(a).all() or not np.isfinite(b).all():
        raise ValueError("At least four finite paired anatomical landmarks required")
    if not .5 <= height_scale <= 2 or not 0 < max_rms_m <= .1:
        raise ValueError("Unsafe registration policy")
    a = a * height_scale
    ca, cb = a.mean(axis=0), b.mean(axis=0)
    if np.linalg.matrix_rank(a-ca) < 3 or np.linalg.matrix_rank(b-cb) < 3:
        raise ValueError("Degenerate landmark frame")
    u, _, vt = np.linalg.svd((a-ca).T @ (b-cb))
    correction = np.diag([1, 1, np.linalg.det(u @ vt)])
    rotation = u @ correction @ vt
    distances = np.linalg.norm((a-ca) @ rotation + cb - b, axis=1)
    rms = float(np.sqrt(np.mean(distances**2)))
    if rms > max_rms_m or distances.max() > 3*max_rms_m:
        raise ValueError("Failed landmark registration")
    registered = Mesh((mesh.vertices*height_scale-ca) @ rotation + cb, mesh.triangles)
    metadata = {"version": "proper-landmark-registration-1", "rms_m": rms, "max_m": float(distances.max()), "height_scale": height_scale,
                "rotation": rotation.tolist(), "source_center": ca.tolist(), "target_center": cb.tolist(), "policy_rms_m": max_rms_m,
                "source_mesh_sha256": digest(mesh.to_dict()), "landmark_sha256": digest([a.tolist(), b.tolist()])}
    return registered, {**metadata, "sha256": digest(metadata)}


def correspondence(source: Mesh, template: Mesh, mapping, *, max_edge_ratio=4.0):
    """No automatic nearest-vertex guess. The reviewed barycentric map is an explicit input."""
    body = {k: v for k, v in mapping.items() if k != "sha256"}
    if mapping.get("sha256") != digest(body) or mapping.get("version") != "barycentric-map-1":
        raise ValueError("Unversioned/changed correspondence map")
    if mapping.get("source_topology") != source.topology_hash or mapping.get("target_topology") != template.topology_hash:
        raise ValueError("Correspondence topology mismatch")
    indices = np.asarray(mapping["source_triangles"])
    weights = np.asarray(mapping["weights"], float)
    if indices.shape != (len(template.vertices),) or not np.issubdtype(indices.dtype, np.integer) or indices.min() < 0 or indices.max() >= len(source.triangles):
        raise ValueError("Incomplete correspondence")
    if weights.shape != (len(indices), 3) or not np.isfinite(weights).all() or (weights < 0).any() or not np.allclose(weights.sum(axis=1), 1, atol=1e-9):
        raise ValueError("Invalid barycentric weights")
    vertices = (source.vertices[source.triangles[indices]] * weights[:, :, None]).sum(axis=1)
    result = Mesh(vertices, template.triangles)
    before, after = template.vertices[template.triangles], result.vertices[result.triangles]
    n0 = np.cross(before[:, 1]-before[:, 0], before[:, 2]-before[:, 0])
    n1 = np.cross(after[:, 1]-after[:, 0], after[:, 2]-after[:, 0])
    if (np.sum(n0*n1, axis=1) <= 0).any():
        raise ValueError("Correspondence flips a canonical face")
    ratios = []
    for a, b in [(0, 1), (1, 2), (2, 0)]:
        ratios.extend(np.linalg.norm(after[:, a]-after[:, b], axis=1) / np.linalg.norm(before[:, a]-before[:, b], axis=1))
    if max_edge_ratio < 1 or max(ratios) > max_edge_ratio or min(ratios) < 1/max_edge_ratio:
        raise ValueError("Unsafe edge distortion")
    return result


def closed_volume(mesh: Mesh):
    """Returns None for open/nonmanifold/inconsistently wound geometry; never silently caps."""
    edges = {}
    for face in mesh.triangles:
        for a, b in zip(face, np.roll(face, -1)):
            key = tuple(sorted((int(a), int(b))))
            count, orientation = edges.get(key, (0, 0))
            edges[key] = (count+1, orientation+(1 if a < b else -1))
    if any(count != 2 or orientation != 0 for count, orientation in edges.values()):
        return None
    t = mesh.vertices[mesh.triangles]
    return float(abs(np.einsum("ij,ij->i", t[:, 0], np.cross(t[:, 1], t[:, 2])).sum())/6)


def cache_artifact(cache: Path, payload):
    if not (cache / ".shape-research-cache").is_file() or any(linked(p) for p in [cache, *cache.parents]):
        raise ValueError("Explicit marked research cache required")
    from .contracts import canonical
    sha = digest(payload)
    target = cache / (sha + ".json")
    if linked(target):
        raise ValueError("Unsafe cache path")
    target.write_bytes(canonical(payload))
    return sha


def delete_cache(cache: Path, execute=False):
    if linked(cache) or not (cache / ".shape-research-cache").is_file():
        raise ValueError("Refusing unmarked cache")
    for parent in cache.parents:
        if linked(parent):
            raise ValueError("Refusing linked cache parent")
    root = cache.resolve(strict=True)
    import re
    files = list(root.iterdir())
    if any(linked(p) or not p.is_file() or (p.name != ".shape-research-cache" and not re.fullmatch(r"[a-f0-9]{64}\.json", p.name)) for p in files):
        raise ValueError("Unexpected cache entry; manual steward review required")
    payloads = [p for p in files if p.name != ".shape-research-cache"]
    if execute:
        for p in payloads:
            p.unlink()
    return {"files": len(payloads), "executed": execute, "secure_erasure": False}
