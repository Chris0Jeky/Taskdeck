#!/usr/bin/env python3
"""Create or verify a versioned SHA-256 manifest for a Taskdeck backup set."""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path

CHUNK = 1024 * 1024


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as handle:
        while block := handle.read(CHUNK):
            digest.update(block)
    return digest.hexdigest()


def create_manifest(root: Path, exclude: set[str] | None = None) -> dict:
    exclude = exclude or set()
    files = []
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root).as_posix()
        if path.is_symlink():
            raise ValueError(f'symlink_not_allowed:{relative}')
        if not path.is_file() or relative in exclude:
            continue
        files.append({'path': relative, 'byte_size': path.stat().st_size, 'sha256': sha256(path)})
    return {
        'schema_version': 1,
        'created_at_utc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'hash_algorithm': 'sha256',
        'files': files,
    }


def verify_manifest(root: Path, manifest: dict) -> list[str]:
    errors: list[str] = []
    if manifest.get('schema_version') != 1 or manifest.get('hash_algorithm') != 'sha256':
        errors.append('manifest_header_invalid')
    seen: set[str] = set()
    for entry in manifest.get('files', []):
        relative = entry.get('path')
        relative_path = Path(relative) if isinstance(relative, str) else None
        if (
            relative_path is None
            or not relative
            or relative in seen
            or relative_path.is_absolute()
            or '..' in relative_path.parts
            or '\\' in relative
        ):
            errors.append(f'path_invalid:{relative}')
            continue
        seen.add(relative)
        path = root / relative_path
        if not path.is_file():
            errors.append(f'file_missing:{relative}')
            continue
        if path.stat().st_size != entry.get('byte_size'):
            errors.append(f'size_mismatch:{relative}')
            continue
        if sha256(path) != entry.get('sha256'):
            errors.append(f'hash_mismatch:{relative}')
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='command', required=True)
    create = sub.add_parser('create')
    create.add_argument('root', type=Path)
    create.add_argument('--out', type=Path, required=True)
    verify = sub.add_parser('verify')
    verify.add_argument('root', type=Path)
    verify.add_argument('--manifest', type=Path, required=True)
    args = parser.parse_args()

    if args.command == 'create':
        manifest = create_manifest(args.root.resolve(), {args.out.name})
        args.out.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
        print(f'ok: {len(manifest["files"])} files')
        return 0

    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    errors = verify_manifest(args.root.resolve(), manifest)
    if errors:
        print('\n'.join(errors))
        return 1
    print(f'ok: {len(manifest.get("files", []))} files verified')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
