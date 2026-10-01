"""Compare separate Git checkouts' deterministic Player traces without masking event-order changes."""

import argparse
import json
import math
from pathlib import Path


def first_difference(expected, actual, path="", visual=False):
    if type(expected) is dict and type(actual) is dict:
        if expected.keys() != actual.keys():
            return path, list(expected), list(actual)
        for key in expected:
            difference = first_difference(expected[key], actual[key], f"{path}.{key}", visual)
            if difference:
                return difference
    elif type(expected) is list and type(actual) is list:
        for index, (left, right) in enumerate(zip(expected, actual)):
            difference = first_difference(left, right, f"{path}[{index}]", visual)
            if difference:
                return difference
        if len(expected) != len(actual):
            return f"{path}.length", len(expected), len(actual)
    elif expected != actual:
        tolerance = 1.0 if visual and path.endswith((".Y", ".Height")) else 1e-8 if path.endswith(".Health") else 0
        if tolerance and type(expected) in (int, float) and type(actual) in (int, float):
            if math.isclose(expected, actual, rel_tol=0, abs_tol=tolerance):
                return None
        return path, expected, actual
    return None


def compare(baseline: Path, current: Path):
    old_files = {p.name: p for p in baseline.glob("*.json")}
    new_files = {p.name: p for p in current.glob("*.json")}
    if not old_files or old_files.keys() != new_files.keys():
        raise ValueError(f"Scenario sets differ or are empty: baseline-only={old_files.keys() - new_files.keys()}, current-only={new_files.keys() - old_files.keys()}")
    results = []
    for name, path in sorted(old_files.items()):
        old = json.loads(path.read_text(encoding="utf-8-sig"))
        new = json.loads(new_files[name].read_text(encoding="utf-8-sig"))
        old_visuals, new_visuals = old.pop("Visuals"), new.pop("Visuals")
        # Compare the ordered rows first so the report identifies the causal checkpoint before aggregate counts.
        behaviour = first_difference(old["Rows"], new["Rows"], "Rows") or first_difference(old, new, "Trace")
        visual = first_difference(old_visuals, new_visuals, "Visuals", visual=True)
        results.append({"scenario": old["Scenario"], "baseline_rows": len(old["Rows"]), "current_rows": len(new["Rows"]),
                        "behaviour_difference": behaviour, "visual_difference": visual})
    return {"scenarios": len(results), "behaviour_matches": sum(r["behaviour_difference"] is None for r in results),
            "visual_matches": sum(r["visual_difference"] is None for r in results), "results": results}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("current", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    report = compare(arguments.baseline, arguments.current)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "results"}))
    for result in report["results"]:
        if result["behaviour_difference"] or result["visual_difference"]:
            print(json.dumps(result, ensure_ascii=False))
    raise SystemExit(0 if report["behaviour_matches"] == report["visual_matches"] == report["scenarios"] else 1)
