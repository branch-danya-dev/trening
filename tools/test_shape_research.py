import copy
from dataclasses import replace
from datetime import date, timedelta
from pathlib import Path
import tempfile
import unittest
import numpy as np

from shape_research.contracts import Composition, LongitudinalBodyPair, digest, split_participants, check_split, require_data_access, contained
from shape_research.geometry import Mesh, import_mesh, rigid_landmark_registration, correspondence, closed_volume, cache_artifact, delete_cache
from shape_research.models import PCA, Support, DeltaShapeInput, ZeroChangeModel, representation_benchmark
from shape_research.metrics import compare, nearest_surface, strata, aggregate
from shape_research.harness import freeze_evaluation, evaluate_registered


def pairs(n=20):
    return [LongitudinalBodyPair(f"subject-{i:04d}", "2026-01-01", "2026-01-29", 28, "Male" if i%2 else "Female", 35, 1.75,
            Composition(80, 20, 60, "synthetic-test-FFM"), Composition(79, 19, 60, "synthetic-test-FFM"), "a.obj", "b.obj", "SYNTHETIC_TEST_ONLY", "1", "a"*64, "b"*64, synthetic=True) for i in range(n)]


def tetra():
    return Mesh([[0., 0., 0.], [1., 0., 0.], [0., 1., 0.], [0., 0., 1.]], [[0, 2, 1], [0, 1, 3], [1, 2, 3], [2, 0, 3]])


def identity_map(m):
    ids, weights = [], []
    for v in range(len(m.vertices)):
        face = next(i for i, t in enumerate(m.triangles) if v in t)
        ids.append(face)
        weights.append([float(x == v) for x in m.triangles[face]])
    value = {"version": "barycentric-map-1", "source_topology": m.topology_hash, "target_topology": m.topology_hash, "source_triangles": ids, "weights": weights}
    return {**value, "sha256": digest(value)}


class ResearchTests(unittest.TestCase):
    def setUp(self):
        self.pairs = pairs()
        self.manifest = split_participants(self.pairs, "fixture-seed")
        self.lookup = check_split(self.manifest, self.pairs)
        self.train = [p.participant_id for p in self.pairs if self.lookup[p.participant_id] == "train"]
        self.rng = np.random.default_rng(37)
        self.x = self.rng.normal(size=(len(self.train), 2)) @ np.array([[1., 0, 1, 2], [0, 2, 1, 0]]) + 4
        self.pca = PCA.fit(self.x, self.train, self.manifest, 2, "test-topology")

    def test_contract_rejects_wrong_horizon_and_compartments(self):
        self.pairs[0].validate()
        with self.assertRaises(ValueError): replace(self.pairs[0], horizon_days=30).validate()
        with self.assertRaises(ValueError): Composition(80, 20, 55, "lean-soft-tissue").validate()

    def test_split_is_order_independent_and_keeps_visits(self):
        repeat = replace(self.pairs[0], t1="2026-02-26", horizon_days=56)
        self.assertEqual(self.manifest, split_participants(list(reversed(self.pairs))+[repeat], "fixture-seed"))

    def test_leakage_fails_even_with_rehashed_manifest(self):
        bad = copy.deepcopy(self.manifest)
        bad["assignments"].append({"participant": self.train[0], "split": "test"})
        bad["sha256"] = digest({k: v for k, v in bad.items() if k != "sha256"})
        with self.assertRaisesRegex(ValueError, "leakage"): check_split(bad, self.pairs)

    def test_unknown_participant_and_split_tampering_fail(self):
        with self.assertRaises(ValueError): check_split(self.manifest, self.pairs[:-1])
        bad = copy.deepcopy(self.manifest); bad["seed"] = "changed"
        with self.assertRaises(ValueError): check_split(bad, self.pairs)

    def test_pca_never_trains_on_validation_or_test(self):
        for split in ["val", "test"]:
            foreign = next(p for p, s in self.lookup.items() if s == split)
            with self.assertRaisesRegex(ValueError, "TRAIN|Training"): PCA.fit(self.x, [foreign]+self.train[1:], self.manifest, 2, "test")

    def test_pca_reconstruction_variance_and_serialization(self):
        np.testing.assert_allclose(self.pca.decode(self.pca.encode(self.x)), self.x, atol=1e-10)
        curve = self.pca.reconstruction_curve(self.x)
        self.assertAlmostEqual(curve[-1]["explained_variance"], 1)
        self.assertGreater(curve[0]["coordinate_rmse_m"], curve[-1]["coordinate_rmse_m"])
        self.assertEqual(self.pca.artifact(), PCA.restore(self.pca.artifact()).artifact())
        with self.assertRaises(ValueError): PCA.fit(self.x, self.train, self.manifest, 3, "test")

    def test_pca_hash_tamper(self):
        artifact = self.pca.artifact(); artifact["mean"][0] += 1
        with self.assertRaises(ValueError): PCA.restore(artifact)

    def test_sex_specific_benchmark_does_not_accept_test(self):
        rows = [{"participant": p, "sex": "Male", "shape": x} for p, x in zip(self.train, self.x)]
        test = next(p for p, s in self.lookup.items() if s == "test")
        with self.assertRaises(ValueError): representation_benchmark(rows, [{"participant": test, "sex": "Male", "shape": self.x[0]}], self.manifest, 2, "test")

    def test_landmarks_recover_proper_rigid_transform(self):
        m = tetra(); shifted = Mesh(m.vertices+[2, 3, 4], m.triangles)
        registered, report = rigid_landmark_registration(shifted, shifted.vertices, m.vertices)
        np.testing.assert_allclose(registered.vertices, m.vertices, atol=1e-12)
        self.assertLess(report["rms_m"], 1e-10)
        with self.assertRaises(ValueError): rigid_landmark_registration(m, np.zeros((4, 3)), m.vertices)
        with self.assertRaises(ValueError): rigid_landmark_registration(m, m.vertices, m.vertices*2)

    def test_representation_report_cannot_swallow_train_leakage(self):
        test = next(p for p, s in self.lookup.items() if s == "test")
        val = next(p for p, s in self.lookup.items() if s == "val")
        rows = [{"participant": test, "sex": "Male", "shape": self.x[0]}]
        with self.assertRaisesRegex(ValueError, "Training"):
            representation_benchmark(rows, [{"participant": val, "sex": "Male", "shape": self.x[0]}], self.manifest, 2, "test")

    def test_support_cannot_mix_split_provenance(self):
        pca = copy.deepcopy(self.pca); pca.split_hash = "other-split"
        inputs = [{"numeric": {"horizon": 28}, "subgroup": "Male"} for _ in self.x]
        with self.assertRaisesRegex(ValueError, "frozen split"):
            Support.fit(pca, self.x, inputs, self.train, self.manifest)

    def test_registration_correspondence_requires_topology_hash(self):
        m = tetra(); mapping = identity_map(m)
        np.testing.assert_equal(correspondence(m, m, mapping).vertices, m.vertices)
        bad = copy.deepcopy(mapping); bad["source_topology"] = "wrong"; bad["sha256"] = digest({k:v for k,v in bad.items() if k != "sha256"})
        with self.assertRaises(ValueError): correspondence(m, m, bad)

    def test_correspondence_rejects_collapsed_shape_and_bad_weights(self):
        m = tetra(); bad = identity_map(m); bad["weights"][0] = [2, -1, 0];bad["sha256"] = digest({k:v for k,v in bad.items() if k != "sha256"})
        with self.assertRaises(ValueError): correspondence(m, m, bad)
        with self.assertRaises(ValueError): Mesh(np.zeros((4, 3)), m.triangles)

    def test_obj_units_and_orientation(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d)/"mesh.obj"
            p.write_text("v 0 0 0\nv 100 0 0\nv 0 100 0\nv 0 0 100\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 3 1 4\n")
            np.testing.assert_allclose(import_mesh(p, "cm").vertices, tetra().vertices)
            with self.assertRaises(ValueError): import_mesh(p, "cm", np.diag([-1, 1, 1]))

    def test_metrics_known_identity_translation_volume_and_surface(self):
        m = tetra(); identity = compare(m, m, rings={"test-ring": [0, 1, 2]}, silhouette_resolution=32)
        for key in ["vertex_mean_m", "surface_mean_m", "girth_mae_cm", "volume_error_l"]: self.assertAlmostEqual(identity[key], 0)
        self.assertEqual(identity["front_iou"], 1)
        self.assertAlmostEqual(closed_volume(m), 1/6)
        np.testing.assert_allclose(nearest_surface([[.2, .2, 0]], m), [0], atol=1e-10)
        moved = compare(Mesh(m.vertices+[.1, 0, 0], m.triangles), m, silhouette_resolution=32)
        self.assertAlmostEqual(moved["vertex_mean_m"], .1)
        self.assertIsNone(moved["girth_mae_cm"])

    def test_support_ood_fallback_not_probability(self):
        inputs = [{"numeric": {"horizon": 28, "deltaFat": -1}, "subgroup": "Male"} for _ in self.x]
        support = Support.fit(self.pca, self.x, inputs, self.train, self.manifest)
        result = support.assess(self.pca, self.x[0], {"horizon": 90, "deltaFat": -30}, "Unknown")
        self.assertTrue(result["fallback"]); self.assertFalse(result["is_probability"])
        self.assertIn("range:horizon", result["reasons"])
        outside = self.x[0].copy(); outside[3] += 100
        self.assertIn("pca-residual", support.assess(self.pca, outside, inputs[0]["numeric"], "Male")["reasons"])

    def test_placeholder_cannot_appear_as_production(self):
        result = ZeroChangeModel().predict(DeltaShapeInput((1., 2.), "Male", 35, 1.75, 80, 20, -1, 0, 28))
        self.assertEqual(result["delta_latent"], [0, 0]); self.assertTrue(result["fallback"])
        self.assertEqual(result["uncertainty"], "UNVALIDATED")

    def test_access_denied_and_no_synthetic_real_gate(self):
        with tempfile.TemporaryDirectory() as d, tempfile.TemporaryDirectory() as repo:
            with self.assertRaisesRegex(ValueError, "BLOCKED"): require_data_access({}, Path(d), self.pairs, self.manifest, Path(repo))
            gate = {k: True for k in ["dataset_obtained", "analysis_permitted", "derivative_rights_understood", "secure_storage_approved", "pseudonymized", "ethics_determination_recorded"]}
            gate.update(version="data-access-gate-1", approval_reference="unit-test", steward_reference="unit-test", storage_country="unit-test", derivative_rights_decision="unit-test", retention_until="2099-01-01", split_sha256=self.manifest["sha256"])
            with self.assertRaisesRegex(ValueError, "Synthetic"): require_data_access(gate, Path(d), self.pairs, self.manifest, Path(repo))

    def test_cache_preview_and_bounded_deletion(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            with self.assertRaises(ValueError): delete_cache(root, True)
            (root/".shape-research-cache").touch();sha = cache_artifact(root, {"synthetic": True})
            self.assertEqual(delete_cache(root)["files"], 1);self.assertTrue((root/(sha+".json")).exists())
            (root/"raw.obj").touch()
            with self.assertRaises(ValueError): delete_cache(root, True)
            (root/"raw.obj").unlink();self.assertEqual(delete_cache(root, True)["files"], 1)
            with self.assertRaises(ValueError): contained(root, "../secret")

    def test_participant_aggregation_and_missingness(self):
        rows = [{"participant": "a", "strata": ["all"], "metrics": {"error": 1}}]*10 + [{"participant": "b", "strata": ["all"], "metrics": {"error": 3}}]
        self.assertEqual(aggregate(rows, 2)["all"]["metrics"]["error"]["participant_mean"], 2)
        self.assertEqual(aggregate(rows)["all"]["status"], "SUPPRESSED_SMALL_CELL")
        self.assertIn("exact:4w", strata(self.pairs[0]))
        self.assertNotIn("exact:4w", strata(replace(self.pairs[0], horizon_days=30)))

    def test_freeze_rejects_test_model_provenance_and_policy_tamper(self):
        policy = dict(surface_improvement_fraction=.1, girth_improvement_fraction=.1, maximum_subgroup_regression_fraction=.1, minimum_test_participants=5)
        receipt = freeze_evaluation(self.manifest, self.pairs, self.pca.artifact(), policy)
        self.assertEqual(len(receipt["sha256"]), 64)
        artifact = self.pca.artifact();artifact["training_participants"].append(next(p for p,s in self.lookup.items() if s == "test"));artifact["sha256"] = digest({k:v for k,v in artifact.items() if k != "sha256"})
        with self.assertRaises(ValueError): freeze_evaluation(self.manifest, self.pairs, artifact, policy)

    def test_held_out_harness_is_paired_and_synthetic_never_go(self):
        policy = dict(surface_improvement_fraction=.1, girth_improvement_fraction=.1, maximum_subgroup_regression_fraction=.1, minimum_test_participants=5, information_policy="t0-only")
        model = self.pca.artifact(); receipt = freeze_evaluation(self.manifest, self.pairs, model, policy)
        m = tetra(); cases = {}
        for p in self.pairs:
            if self.lookup[p.participant_id] != "test": continue
            cases[digest([p.participant_id,p.t0,p.t1])] = {"actual":m.to_dict(), "candidate":m.to_dict(), "model_hash":model["sha256"],
                "t0_source_sha256":p.mesh_sha256_t0,"t1_source_sha256":p.mesh_sha256_t1,"makehuman_to_research":identity_map(m),
                "procedural":{"version":"procedural-research-bridge-1","shapeVersion":"body-shape-procedural-1","horizonDays":28,"synthetic":True,"informationPolicy":"t0-only","endpoint":m.to_dict()}}
        kwargs=dict(pairs=self.pairs, manifest=self.manifest, frozen=receipt, model_artifact=model, policy=policy, gate={}, data_root=Path('.'),repo_root=Path('.'),cases=cases,synthetic_test=True)
        result = evaluate_registered(**kwargs)
        self.assertFalse(result['production_go']);self.assertEqual(result['evidence'],'SYNTHETIC_TEST_ONLY')
        with self.assertRaises(ValueError): evaluate_registered(**{**kwargs,'cases':{}})
        changed=dict(policy);changed['girth_improvement_fraction']=.01
        with self.assertRaises(ValueError): evaluate_registered(**{**kwargs,'policy':changed})


if __name__ == "__main__":
    unittest.main()
