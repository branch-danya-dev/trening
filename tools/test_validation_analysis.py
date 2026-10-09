import copy
import unittest
from validation_analysis import summarize


def package(error=1):
    return {"format": "trening-validation", "schemaVersion": 1, "synthetic": True, "hypotheses": [{
        "reference": "hypothesis-1", "localStartDate": "2026-01-01", "targetDate": "2026-01-31", "horizonDays": 30,
        "modelVersion": "test-model", "evidenceCutoff": "2026-01-01T10:00:00Z",
        "origin": {"createdAt": "2026-01-01T10:00:00Z", "reconstructed": False, "baseline": {"sex": "Male", "age": 30, "heightCm": 180, "weightKg": 80}},
        "evidence": {"activity": {"factualDays": 12}, "nutrition": {"eligibleDayCount": 10}},
        "endpoint": {"weightRange": {"lower": 78, "upper": 80}},
        "outcome": {"observedAt": "2026-01-31", "recordedAt": "2026-01-31T10:00:00Z", "timing": "Exact", "source": "Manual",
                    "rows": [{"metric": "WeightKg", "actual": 79+error, "predicted": 79, "sourceQuality": 1, "horizonDays": 30}]}}]}


def intake(n=6):
    return {"version": "prospective-intake-1", "data_kind": "synthetic-test", "participants": [{"study_id": f"study-{i:03d}", "consent_confirmed": True} for i in range(n)]}


class ValidationAnalysisTests(unittest.TestCase):
    def test_known_bias_mae_range_and_coverage_are_only_synthetic(self):
        result = summarize(intake(), [package(1 if i%2 else -1) for i in range(6)])
        row = next(r for r in result["metrics"] if r["group"] == "all")
        self.assertEqual(row["mae"], 1); self.assertEqual(row["bias"], 0); self.assertEqual(row["range_coverage"], 1)
        self.assertEqual(result["coverage"]["activity"]["mean_days"], 12)
        self.assertEqual(result["status"], "SYNTHETIC_TEST_ONLY")

    def test_duplicate_exports_rejected(self):
        manifest = intake();manifest["participants"][1] = manifest["participants"][0]
        with self.assertRaises(ValueError): summarize(manifest, [package() for _ in range(6)])

    def test_same_fact_does_not_multiply_evidence(self):
        data = package();data["hypotheses"].append(copy.deepcopy(data["hypotheses"][0]))
        result = summarize(intake(1), [data], minimum=1)
        self.assertEqual(next(r for r in result["metrics"] if r["group"] == "all")["observations"], 1)
        self.assertEqual(result["exclusions"]["same-day-metric-duplicate"], 1)

    def test_missing_legacy_retrospective_and_late_are_excluded(self):
        data = [package() for _ in range(4)]
        data[0]["hypotheses"][0].pop("outcome")
        data[1]["hypotheses"][0].pop("origin")
        data[2]["hypotheses"][0]["origin"]["reconstructed"] = True
        data[3]["hypotheses"][0]["outcome"].update(observedAt="2026-02-01", timing="Late")
        result = summarize(intake(4), data)
        self.assertEqual(result["metrics"], []);self.assertEqual(result["missing_outcomes"], 1)
        self.assertEqual(result["exclusions"]["origin-unverified-or-retrospective"], 2)

    def test_synthetic_cannot_be_mislabeled_real(self):
        manifest = intake(1);manifest["data_kind"] = "prospective-consented"
        with self.assertRaises(ValueError): summarize(manifest, [package()])

    def test_empty_real_report_has_no_accuracy(self):
        manifest = intake(0);manifest["data_kind"] = "prospective-consented"
        result = summarize(manifest, [])
        self.assertEqual(result["status"], "NO_PROSPECTIVE_DATA");self.assertEqual(result["metrics"], [])

    def test_small_cells_and_missing_ranges(self):
        result = summarize(intake(1), [package()])
        self.assertTrue(all(r["status"] == "SUPPRESSED_SMALL_CELL" for r in result["metrics"]))
        data = package();data["hypotheses"][0].pop("endpoint")
        result = summarize(intake(1), [data], minimum=1)
        self.assertIsNone(result["metrics"][0]["range_coverage"])

    def test_wrong_horizon_and_nonfinite_never_aggregate(self):
        data = package();row = data["hypotheses"][0]["outcome"]["rows"][0]
        row["horizonDays"] = 28
        self.assertEqual(summarize(intake(1), [data])["metrics"], [])
        row.update(horizonDays=30, actual=float("nan"))
        self.assertEqual(summarize(intake(1), [data])["metrics"], [])


if __name__ == "__main__": unittest.main()
