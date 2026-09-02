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
  ``executionPerformed = true``; a runtime metrics report carries no content-bearing keys (a
  key-shape audit only: string *values* are not checked against the CF-24B frozen metric and
  reason-code dictionaries, which do not exist until CF-24B lands); a policy snapshot's
  ``policyDigest`` is the SHA-256 of its canonical ``policy`` JSON.

Usage (repo root)::

    py -3 -B scripts/context_fabric/check_contract_drafts.py
    py -3 -B -m unittest discover -s scripts/context_fabric -p "test_check_contract_drafts.py"

Requires the ``jsonschema`` package (>= 4.18, for the ``referencing`` registry). A missing
dependency is a failure, not a skip: install it or do not claim the check green.
"""

from __future__ import annotations

import argparse
import re
import hashlib
import json
import re
import sys
from datetime import datetime
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

# Key words that mark a content-bearing field in a metric fact or report (CF-24B). Keys are split into
# words (camelCase and snake_case), so ``contextBindingStatus`` is not caught by ``text`` and a word is
# only flagged when it stands alone: ``evidenceQuote`` fails, ``quoteCount`` passes because the key also
# carries a shape word that says it holds a number or an identifier, never the content itself. A bare
# ``name`` is allowed because a metric's dictionary name is a code, not user content; ``speakerName``
# is still caught through ``speaker``. This is a guard on the drafts, not the allowlisted schema CF-24B
# must ship (its ``RUNTIME_METRICS.md`` rule).
CONTENT_WORDS = frozenset(
    {"text", "prompt", "quote", "transcript", "filename", "url", "message", "description", "title",
     "content", "bytes", "speaker", "body", "excerpt", "snippet"}
)
CONTENT_SAFE_SHAPE_WORDS = frozenset(
    {"hash", "digest", "id", "ids", "count", "status", "kind", "length", "size", "ms", "rate", "class",
     "code", "codes", "version", "state", "bucket", "sha256", "known", "present", "included"}
)
_WORD_SPLIT = re.compile(r"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")


def key_words(key: str) -> list[str]:
    return [word.lower() for word in _WORD_SPLIT.findall(key.replace("-", "_").replace("_", " "))]


def canonical_json(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def canonical_digest(value: Any) -> str:
    return "sha256:" + hashlib.sha256(canonical_json(value)).hexdigest()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


RFC3339_DATE_TIME = re.compile(
    r"\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[Zz]|[+-]\d{2}:\d{2})"
)


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
            words = key_words(key)
            content_bearing = any(word in CONTENT_WORDS for word in words) or "".join(words) in CONTENT_WORDS
            if content_bearing and not any(word in CONTENT_SAFE_SHAPE_WORDS for word in words):
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


def build_format_checker():
    """``format`` is annotation-only in jsonschema unless a checker is supplied. ``uuid`` is built in;
    ``date-time`` is enforced here without the optional ``rfc3339-validator`` dependency."""
    from jsonschema import Draft202012Validator

    checker = Draft202012Validator.FORMAT_CHECKER

    @checker.checks("date-time", raises=ValueError)
    def _is_date_time(instance: Any) -> bool:
        if not isinstance(instance, str):
            return True
        # RFC 3339 shape first: ``fromisoformat`` alone accepts a bare local time or a space
        # separator, which JSON Schema ``date-time`` does not.
        if not RFC3339_DATE_TIME.fullmatch(instance):
            raise ValueError(f"{instance!r} is not an RFC 3339 date-time (needs 'T' and a UTC offset)")
        datetime.fromisoformat(instance[:-1] + "+00:00" if instance[-1] in "zZ" else instance)
        return True

    return checker


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
    format_checker = build_format_checker()

    for entry in manifest["contracts"]:
        schema_path = base / entry["schema"]
        schema = load_json(schema_path)
        try:
            Draft202012Validator.check_schema(schema)
        except Exception as exc:  # noqa: BLE001 - report every schema defect
            errors.append(f"{entry['schema']}: invalid schema: {exc}")
            continue
        rule_names = entry.get("semantic", [])
        unknown_rules = [name for name in rule_names if name not in SEMANTIC_RULES]
        for name in unknown_rules:
            errors.append(f"{entry['schema']}: unknown semantic rule {name}")
        rules = [SEMANTIC_RULES[name] for name in rule_names if name in SEMANTIC_RULES]
        validator = Draft202012Validator(schema, registry=registry, format_checker=format_checker)
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
                for rule_name, rule in zip([n for n in rule_names if n in SEMANTIC_RULES], rules):
                    for problem in rule(document):
                        errors.append(f"{fixture_rel}{prefix}: {rule_name}: {problem}")
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
