#!/usr/bin/env python3
"""Validate owned, expiring CI quarantine entries."""
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path

REQUIRED = {'test', 'issue', 'owner', 'reason', 'created_on', 'expires_on', 'compensating_coverage'}


def parse_date(value: object, field: str, errors: list[str], index: int) -> dt.date | None:
    if not isinstance(value, str):
        errors.append(f'entry[{index}].{field}:invalid')
        return None
    try:
        return dt.date.fromisoformat(value)
    except ValueError:
        errors.append(f'entry[{index}].{field}:invalid')
        return None


def validate_document(document: dict, today: dt.date | None = None, hard_max_days: int = 30) -> list[str]:
    today = today or dt.date.today()
    errors: list[str] = []
    entries = document.get('entries')
    if document.get('schema_version') != 1:
        errors.append('schema_version:expected_1')
    if not isinstance(entries, list):
        return errors + ['entries:must_be_array']

    seen: set[str] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            errors.append(f'entry[{index}]:must_be_object')
            continue
        missing = REQUIRED - entry.keys()
        for field in sorted(missing):
            errors.append(f'entry[{index}].{field}:required')

        test = entry.get('test')
        if not isinstance(test, str) or not test.strip():
            errors.append(f'entry[{index}].test:invalid')
        elif test in seen:
            errors.append(f'entry[{index}].test:duplicate')
        else:
            seen.add(test)

        issue = entry.get('issue')
        if not isinstance(issue, str) or not (issue.startswith('#') or issue.startswith('https://github.com/')):
            errors.append(f'entry[{index}].issue:invalid')

        owner = entry.get('owner')
        if not isinstance(owner, str) or len(owner.strip()) < 2:
            errors.append(f'entry[{index}].owner:invalid')

        reason = entry.get('reason')
        if not isinstance(reason, str) or len(reason.strip()) < 12:
            errors.append(f'entry[{index}].reason:too_short')

        coverage = entry.get('compensating_coverage')
        if not isinstance(coverage, str) or len(coverage.strip()) < 5:
            errors.append(f'entry[{index}].compensating_coverage:invalid')

        created = parse_date(entry.get('created_on'), 'created_on', errors, index)
        expires = parse_date(entry.get('expires_on'), 'expires_on', errors, index)
        if created and expires:
            if expires < created:
                errors.append(f'entry[{index}].expires_on:before_created')
            if expires < today:
                errors.append(f'entry[{index}].expires_on:expired')
            if (expires - created).days > hard_max_days and not entry.get('maintainer_exception'):
                errors.append(f'entry[{index}].expires_on:exceeds_{hard_max_days}_days')

        if isinstance(test, str) and ('*' in test or '?' in test) and not isinstance(entry.get('maximum_matches'), int):
            errors.append(f'entry[{index}].maximum_matches:required_for_wildcard')

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('path', type=Path)
    parser.add_argument('--today', type=dt.date.fromisoformat)
    parser.add_argument('--hard-max-days', type=int, default=30)
    args = parser.parse_args()

    document = json.loads(args.path.read_text(encoding='utf-8'))
    errors = validate_document(document, args.today, args.hard_max_days)
    if errors:
        print('\n'.join(errors))
        return 1
    print(f'ok: {len(document.get("entries", []))} quarantine entries')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
