import numpy as np
from .geometry import Mesh, closed_volume


def nearest_surface(points, mesh: Mesh):
    """Exact point-to-triangle minimum, bounded blocks. Offline reference, not a runtime hot path."""
    result = []
    for start in range(0, len(points), 64):
        p = np.asarray(points[start:start+64])[:, None, :]
        best = np.full(len(p), np.inf)
        for offset in range(0, len(mesh.triangles), 512):
            t = mesh.vertices[mesh.triangles[offset:offset+512]]
            a, b, c = t[:, 0], t[:, 1], t[:, 2]
            ab, ac = b-a, c-a
            normal = np.cross(ab, ac)
            n2 = np.sum(normal**2, axis=-1)
            distance = np.sum((p-a)*normal, axis=-1)
            projected = p-distance[..., None]*normal/n2[:, None]
            ap = projected-a
            d00, d01, d11 = np.sum(ab*ab, axis=-1), np.sum(ab*ac, axis=-1), np.sum(ac*ac, axis=-1)
            d20, d21 = np.sum(ap*ab, axis=-1), np.sum(ap*ac, axis=-1)
            denominator = d00*d11-d01*d01
            v, w = (d11*d20-d01*d21)/denominator, (d00*d21-d01*d20)/denominator
            inside = (v >= 0) & (w >= 0) & (v+w <= 1)
            d = np.where(inside, distance**2/n2, np.inf)
            for e0, e1 in [(a, b), (b, c), (c, a)]:
                edge = e1-e0
                fraction = np.clip(np.sum((p-e0)*edge, axis=-1)/np.sum(edge**2, axis=-1), 0, 1)
                d = np.minimum(d, np.sum((p-e0-fraction[..., None]*edge)**2, axis=-1))
            best = np.minimum(best, d.min(axis=1))
        result.extend(np.sqrt(np.maximum(0, best)))
    return np.asarray(result)


def silhouette(mesh, axis, bounds, resolution=128):
    if axis not in (0, 2) or not 8 <= resolution <= 512:
        raise ValueError("Unsupported projection policy")
    coordinates = mesh.vertices[:, [axis, 1]]
    low, high = bounds
    scale = (resolution-1)/(high-low)
    vertices = (coordinates-low)*scale
    mask = np.zeros((resolution, resolution), bool)
    for tri in vertices[mesh.triangles]:
        x0, y0 = np.maximum(0, np.floor(tri.min(axis=0)).astype(int))
        x1, y1 = np.minimum(resolution-1, np.ceil(tri.max(axis=0)).astype(int))
        if x0 > x1 or y0 > y1:
            continue
        xx, yy = np.meshgrid(np.arange(x0, x1+1), np.arange(y0, y1+1))
        a, b, c = tri
        den = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den) < 1e-12:
            continue
        u = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        v = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
        mask[y0:y1+1, x0:x1+1] |= (u >= 0) & (v >= 0) & (u+v <= 1)
    return mask


def contour(mask):
    interior = mask.copy()
    for axis in (0, 1):
        for direction in (-1, 1):
            interior &= np.roll(mask, direction, axis)
    interior[[0, -1], :] = False
    interior[:, [0, -1]] = False
    return np.argwhere(mask & ~interior)[:, ::-1]


def nearest_points(a, b):
    return np.concatenate([np.sqrt(((a[i:i+64, None, :]-b[None, :, :])**2).sum(axis=-1).min(axis=1)) for i in range(0, len(a), 64)])


def compare(predicted: Mesh, actual: Mesh, *, rings=None, regions=None, silhouette_resolution=128, fat_kg=None, ffm_kg=None):
    if predicted.topology_hash != actual.topology_hash:
        raise ValueError("Vertex metrics require explicit common topology")
    vertex = np.linalg.norm(predicted.vertices-actual.vertices, axis=1)
    surface = np.concatenate([nearest_surface(predicted.vertices, actual), nearest_surface(actual.vertices, predicted)])
    result = {"vertex_mean_m": float(vertex.mean()), "vertex_median_m": float(np.median(vertex)), "vertex_p95_m": float(np.percentile(vertex, 95)),
              "surface_mean_m": float(surface.mean()), "surface_p95_m": float(np.percentile(surface, 95)), "surface_method": "symmetric-all-vertices-to-triangles-1"}
    for name, axis in [("front", 0), ("side", 2), ("back", 0)]:
        both = np.concatenate([predicted.vertices[:, [axis, 1]], actual.vertices[:, [axis, 1]]])
        low, high = both.min(axis=0)-.01, both.max(axis=0)+.01
        ma, mb = [silhouette(m, axis, (low, high), silhouette_resolution) for m in (predicted, actual)]
        union = (ma | mb).sum()
        ca, cb = contour(ma), contour(mb)
        result[name+"_iou"] = float((ma & mb).sum()/union) if union else None
        scale = (high-low)/(silhouette_resolution-1)
        result[name+"_contour_m"] = float(np.concatenate([nearest_points(ca*scale, cb*scale), nearest_points(cb*scale, ca*scale)]).mean()) if len(ca) and len(cb) else None
    result["silhouette_policy"] = f"orthographic-shared-bounds-{silhouette_resolution}; front/back equivalent binary projection"
    girths = {}
    for name, ids in (rings or {}).items():
        if len(set(ids)) < 3 or min(ids) < 0 or max(ids) >= len(actual.vertices):
            raise ValueError("Invalid ordered girth ring")
        def perimeter(m):
            v = m.vertices[ids]
            return float(np.linalg.norm(v-np.roll(v, 1, axis=0), axis=1).sum())*100
        girths[name] = perimeter(predicted)-perimeter(actual)
    result["girth_errors_cm"] = girths
    result.update({"girth_abs_cm:"+k: abs(v) for k, v in girths.items()})
    result["girth_mae_cm"] = float(np.mean(np.abs(list(girths.values())))) if girths else None
    vp, va = closed_volume(predicted), closed_volume(actual)
    result["volume_error_l"] = (vp-va)*1000 if vp is not None and va is not None else None
    result["composition_volume_residual_l"] = vp*1000-(fat_kg/.9+ffm_kg/1.1) if vp is not None and fat_kg is not None and ffm_kg is not None else None
    result["composition_volume_policy"] = "two-compartment density diagnostic; not DXA equality"
    result["regional_volume_errors_l"] = {}
    for name, faces in (regions or {}).items():
        p, a = [closed_volume(Mesh(m.vertices, np.asarray(faces, dtype=np.int64))) for m in (predicted, actual)]
        result["regional_volume_errors_l"][name] = (p-a)*1000 if p is not None and a is not None else None
        result["regional_volume_abs_l:"+name] = abs(p-a)*1000 if p is not None and a is not None else None
    result["unavailable"] = [name for name in ("girth_mae_cm", "volume_error_l", "composition_volume_residual_l") if result[name] is None]
    return result


def strata(pair):
    days = pair.horizon_days
    bmi = pair.composition_t0.weight_kg/pair.height_m**2
    labels = ["all", "horizon:"+("<=6w" if days <= 42 else "7-12w" if days <= 84 else ">12w"), "sex:"+pair.sex,
              "bmi:"+("<18.5" if bmi < 18.5 else "18.5-25" if bmi < 25 else "25-30" if bmi < 30 else ">=30"),
              "age:"+("18-39" if pair.age_t0 < 40 else "40-59" if pair.age_t0 < 60 else "60+"),
              "fat:"+("loss" if pair.delta_fat < 0 else "gain" if pair.delta_fat > 0 else "stable"),
              "strength:"+("unknown" if pair.strength is None else "yes" if pair.strength else "no")]
    if days in (28, 56, 84):
        labels.append(f"exact:{days//7}w")
    return labels


def aggregate(rows, minimum_participants=5):
    """Participant macro-averages prevent repeated visits dominating; missing != zero."""
    groups = {}
    for row in rows:
        for label in row["strata"]:
            groups.setdefault(label, []).append(row)
    result = {}
    for label, cases in groups.items():
        people = {c["participant"] for c in cases}
        if len(people) < minimum_participants:
            result[label] = {"status": "SUPPRESSED_SMALL_CELL"}
            continue
        metrics = {}
        keys = sorted(set().union(*(c["metrics"] for c in cases)))
        for key in keys:
            values = []
            for p in people:
                present = [c["metrics"].get(key) for c in cases if c["participant"] == p]
                present = [v for v in present if isinstance(v, (float, int)) and not isinstance(v, bool) and np.isfinite(v)]
                if present:
                    values.append(float(np.mean(present)))
            metrics[key] = {"participant_mean": float(np.mean(values)) if values else None, "participants": len(values)}
        result[label] = {"participants": len(people), "pairs": len(cases), "metrics": metrics,
                         "fallback_count": sum(c.get("fallback", False) for c in cases)}
    return result
