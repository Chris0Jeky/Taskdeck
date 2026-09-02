#!/usr/bin/env python3
"""Prove that an explicit API test-class shard manifest is an exact partition."""
from __future__ import annotations

import argparse
import json
from pathlib import Path


def validate(inventory: list[str], manifest: dict) -> list[str]:
    errors: list[str] = []
    inventory_set = set(inventory)
    if len(inventory_set) != len(inventory):
        errors.append('inventory_contains_duplicates')

    assignments: dict[str, list[str]] = {}
    shard_names: set[str] = set()
    for shard in manifest.get('shards', []):
        name = shard.get('name')
        tests = shard.get('tests')
        if not isinstance(name, str) or not name:
            errors.append('shard_name_invalid')
            continue
        if name in shard_names:
            errors.append(f'shard_name_duplicate:{name}')
        shard_names.add(name)
        if not isinstance(tests, list):
            errors.append(f'shard_tests_invalid:{name}')
            continue
        for test in tests:
            if not isinstance(test, str) or not test:
                errors.append(f'test_name_invalid:{name}')
                continue
            assignments.setdefault(test, []).append(name)

    for test in sorted(inventory_set):
        owners = assignments.get(test, [])
        if not owners:
            errors.append(f'test_missing:{test}')
        elif len(owners) > 1:
            errors.append(f'test_duplicate:{test}:{",".join(sorted(owners))}')

    for test in sorted(assignments):
        if test not in inventory_set:
            errors.append(f'test_unknown:{test}')

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--inventory', type=Path, required=True, help='JSON array or {"tests": [...]}')
    parser.add_argument('--manifest', type=Path, required=True)
    args = parser.parse_args()

    inventory_doc = json.loads(args.inventory.read_text(encoding='utf-8'))
    inventory = inventory_doc['tests'] if isinstance(inventory_doc, dict) else inventory_doc
    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    errors = validate(inventory, manifest)
    if errors:
        for error in errors:
            print(error)
        return 1
    print(f'ok: {len(inventory)} tests partitioned across {len(manifest.get("shards", []))} shards')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
