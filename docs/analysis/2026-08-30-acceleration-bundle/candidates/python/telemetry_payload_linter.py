#!/usr/bin/env python3
"""Reject telemetry payload fields outside an explicit content-free allowlist."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

DEFAULT_DENIED_FRAGMENTS = {
    'title', 'description', 'content', 'text', 'prompt', 'message', 'body', 'path',
    'email', 'phone', 'address', 'token', 'secret', 'password', 'api_key', 'apikey',
    'ip_address', 'hostname', 'username', 'clipboard', 'source_url',
}


def leaf_paths(value: Any, prefix: str = '') -> list[tuple[str, Any]]:
    if isinstance(value, dict):
        result: list[tuple[str, Any]] = []
        for key, child in value.items():
            path = f'{prefix}.{key}' if prefix else str(key)
            result.extend(leaf_paths(child, path))
        return result
    if isinstance(value, list):
        result = []
        for index, child in enumerate(value):
            result.extend(leaf_paths(child, f'{prefix}[]'))
        return result
    return [(prefix, value)]


def lint(payload: dict, allowed_paths: set[str], denied_fragments: set[str] | None = None) -> list[str]:
    denied_fragments = denied_fragments or DEFAULT_DENIED_FRAGMENTS
    errors: list[str] = []
    for path, value in leaf_paths(payload):
        normalized = path.lower()
        if path not in allowed_paths:
            errors.append(f'field_not_allowed:{path}')
        if any(fragment in normalized for fragment in denied_fragments):
            errors.append(f'field_denied:{path}')
        if isinstance(value, str) and len(value) > 128:
            errors.append(f'string_too_long:{path}')

    installation_id = payload.get('installation_id')
    if installation_id is not None and not re.fullmatch(r'[0-9a-f]{64}', str(installation_id)):
        errors.append('installation_id_invalid')
    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('payload', type=Path)
    parser.add_argument('--policy', type=Path, required=True)
    args = parser.parse_args()

    payload = json.loads(args.payload.read_text(encoding='utf-8'))
    policy = json.loads(args.policy.read_text(encoding='utf-8'))
    errors = lint(payload, set(policy.get('allowed_paths', [])), set(policy.get('denied_fragments', DEFAULT_DENIED_FRAGMENTS)))
    if errors:
        print('\n'.join(errors))
        return 1
    print('ok: payload is within the explicit allowlist')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
