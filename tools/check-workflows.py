"""Parses every workflow and lints the two rules we rely on: actions pinned to a commit SHA, and no
tag/branch-derived value pasted into a `run:` script (it must go through `env:`). Needs PyYAML.
Usage: python tools/check-workflows.py   (exit code 1 on a problem)"""
import glob
import re
import sys

import yaml

UNSAFE = re.compile(r"\$\{\{\s*(github\.(ref_name|head_ref|ref)|steps\.[\w-]+\.outputs\.[\w-]+|inputs\.[\w-]+)\s*\}\}")
PINNED = re.compile(r"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$")


def problems(path):
    with open(path, encoding="utf-8") as f:
        doc = yaml.safe_load(f)
    jobs = doc.get("jobs") if isinstance(doc, dict) else None
    if not jobs:
        yield f"{path}: no jobs"
        return
    for name, job in jobs.items():
        uses = [job["uses"]] if "uses" in job else []
        for step in job.get("steps", []):
            if "uses" in step:
                uses.append(step["uses"])
            if "run" in step and UNSAFE.search(str(step["run"])):
                yield f"{path}: job {name}: a tag/branch/output value is pasted into run: {step.get('name', step['run'][:40])!r}"
        for u in uses:
            if not (u.startswith("./") or PINNED.match(u)):
                yield f"{path}: job {name}: action not pinned to a SHA: {u}"


def main():
    found = [p for f in sorted(glob.glob(".github/workflows/*.yml")) for p in problems(f)]
    print("\n".join(found) or "workflows ok")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
