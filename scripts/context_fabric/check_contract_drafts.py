"""Validate the Context Fabric contract-draft schemas and fixtures.

The drafts live under ``docs/analysis/2026-08-30-acceleration-bundles-v0.5-v0.6`` and are
planning input for the v0.5 / v0.6 Context Fabric issues (ADR-0065). They are not shipped
contracts; when an issue adopts one, the schema moves next to the code that reads it
(``backend/src/Taskdeck.Application/Processing/Schemas/`` is the precedent) and this manifest
entry is retired.

What this check proves, per manifest entry:

* every schema is a valid JSON Schema draft 2020-12 document (relative ``$ref``s resolve);
* every listed fixture validates against its schema (a fixture file holding a list of documents
  for an object schema validates document by document);
* the semantic rules the bundle's own validators enforced: a route receipt names exactly one
  chosen alternative and no duplicate processors; an authority shadow receipt never reports
  ``executionPerformed = true``; a runtime metrics report carries no content-bearing keys; a
  policy snapshot's ``policyDigest`` is the SHA-256 of its canonical ``policy`` JSON.

Usage (repo root)::

    py -3 -B scripts/context_fabric/check_contract_drafts.py
    py -3 -B -m unittest discover -s scripts/context_fabric -p "test_check_contract_drafts.py"

Requires the ``jsonschema`` package (>= 4.18, for the ``referencing`` registry). A missing
dependency is a failure, not a skip: install it or do not claim the check green.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = (
    REPO_ROOT
    / "docs"
    / "analysis"
    / "2026-08-30-acceleration-bundles-v0.5-v0.6"
    / "contracts.manifest.json"
)

# Key fragments that must never appear in a content-free metric fact or report (CF-24B).
FORBIDDEN_METRIC_KEY_FRAGMENTS = (
    "text",
    "prompt",
    "quote",
    "transcript",
    "filename",
    "file_name",
    "url",
    "message",
    "description",
    "title",
    "content",
    "sourcebytes",
    "speakername",
    "speaker_name",
)


def canonical_json(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def canonical_digest(value: Any) -> str:
    return "sha256:" + hashlib.sha256(canonical_json(value)).hexdigest()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def semantic_route_receipt(value: dict) -> list[str]:
    errors: list[str] = []
    alternatives = value.get("alternatives", [])
    chosen = [item for item in alternatives if item.get("eligibility") == "chosen"]
    chosen_id = value.get("chosenProcessorId")
    if chosen_id is None and chosen:
        errors.append("chosen alternative exists while chosenProcessorId is null")
    if chosen_id is not None and (len(chosen) != 1 or chosen[0].get("processorId") != chosen_id):
        errors.append("chosenProcessorId does not match exactly one chosen alternative")
    ids = [item.get("processorId") for item in alternatives]
    if len(ids) != len(set(ids)):
        errors.append("duplicate processor alternatives")
    if value.get("forcedRerun") and not value.get("forcedRerunReason"):
        errors.append("forced rerun requires forcedRerunReason")
    return errors


def semantic_authority_shadow_receipt(value: dict) -> list[str]:
    if value.get("executionPerformed") is not False:
        return ["authority shadow receipt must record executionPerformed = false (CF-22 is shadow-only)"]
    return []


def semantic_content_free(value: Any, path: str = "$") -> list[str]:
    findings: list[str] = []
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = key.replace("-", "_").lower()
            if any(fragment in normalized for fragment in FORBIDDEN_METRIC_KEY_FRAGMENTS):
                findings.append(f"{path}.{key}: content-bearing key in a metric document")
            findings.extend(semantic_content_free(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            findings.extend(semantic_content_free(child, f"{path}[{index}]"))
    return findings


def semantic_policy_snapshot(value: dict) -> list[str]:
    expected = canonical_digest(value.get("policy"))
    if value.get("policyDigest") != expected:
        return [f"policyDigest {value.get('policyDigest')} != canonical digest {expected}"]
    return []


SEMANTIC_RULES = {
    "route-receipt": semantic_route_receipt,
    "authority-shadow-receipt": semantic_authority_shadow_receipt,
    "content-free": semantic_content_free,
    "policy-snapshot-digest": semantic_policy_snapshot,
}


def build_registry(schema_paths: list[Path]):
    """Register every schema under its ``$id`` and its file name so relative ``$ref``s resolve."""
    from referencing import Registry, Resource
    from referencing.jsonschema import DRAFT202012

    registry = Registry()
    for path in schema_paths:
        schema = load_json(path)
        resource = Resource(contents=schema, specification=DRAFT202012)
        registry = registry.with_resource(path.name, resource)
        if isinstance(schema.get("$id"), str):
            registry = registry.with_resource(schema["$id"], resource)
    return registry


def check(manifest_path: Path) -> tuple[list[str], list[str]]:
    """Return (errors, report_lines)."""
    try:
        from jsonschema import Draft202012Validator
    except ImportError as exc:  # pragma: no cover - environment guard
        return [f"jsonschema is not installed ({exc}); install it before claiming this check green"], []

    manifest = load_json(manifest_path)
    base = manifest_path.parent
    errors: list[str] = []
    report: list[str] = []

    all_schema_paths = [base / entry["schema"] for entry in manifest["contracts"]]
    for path in all_schema_paths:
        if not path.is_file():
            errors.append(f"missing schema: {path.relative_to(base)}")
    if errors:
        return errors, report

    registry = build_registry(all_schema_paths)

    for entry in manifest["contracts"]:
        schema_path = base / entry["schema"]
        schema = load_json(schema_path)
        try:
            Draft202012Validator.check_schema(schema)
        except Exception as exc:  # noqa: BLE001 - report every schema defect
            errors.append(f"{entry['schema']}: invalid schema: {exc}")
            continue
        validator = Draft202012Validator(schema, registry=registry)
        fixtures = entry.get("fixtures", [])
        for fixture_rel in fixtures:
            fixture_path = base / fixture_rel
            if not fixture_path.is_file():
                errors.append(f"{entry['schema']}: missing fixture {fixture_rel}")
                continue
            value = load_json(fixture_path)
            # A fixture file may hold a list of documents for an object schema; validate each one.
            is_document_list = isinstance(value, list) and schema.get("type") == "object"
            documents = value if is_document_list else [value]
            for index, document in enumerate(documents):
                prefix = f"[{index}]" if is_document_list else ""
                for problem in validator.iter_errors(document):
                    errors.append(f"{fixture_rel}{prefix}: {problem.json_path}: {problem.message}")
            for rule_name in entry.get("semantic", []):
                rule = SEMANTIC_RULES.get(rule_name)
                if rule is None:
                    errors.append(f"{entry['schema']}: unknown semantic rule {rule_name}")
                    continue
                for problem in rule(value):
                    errors.append(f"{fixture_rel}: {rule_name}: {problem}")
        report.append(
            f"{entry['schema']}: schema ok; fixtures={len(fixtures)}; semantic={','.join(entry.get('semantic', [])) or '-'}"
        )
    return errors, report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("manifest", nargs="?", default=str(DEFAULT_MANIFEST))
    args = parser.parse_args(argv)
    errors, report = check(Path(args.manifest))
    for line in report:
        print(line)
    if errors:
        print("contract drafts: FAIL", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    print(f"contract drafts: PASS ({len(report)} contracts)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
