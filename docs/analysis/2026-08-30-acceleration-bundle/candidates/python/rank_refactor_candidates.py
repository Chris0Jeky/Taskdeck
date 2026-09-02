#!/usr/bin/env python3
"""Rank source files by an explainable size × churn × touching-commit score."""
from __future__ import annotations

import argparse
import csv
import json
import math
import subprocess
from collections import defaultdict
from pathlib import Path
from typing import Iterable

DEFAULT_EXTENSIONS = {'.cs', '.ts', '.tsx', '.vue', '.js', '.mjs', '.cjs', '.py', '.css', '.scss'}
DEFAULT_EXCLUDES = (
    'node_modules/', 'dist/', 'build/', 'bin/', 'obj/', 'coverage/',
    'Migrations/', '.min.js', '.generated.', 'package-lock.json', 'pnpm-lock.yaml',
)


def run_git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        ['git', '-C', str(repo), *args],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    return result.stdout


def tracked_files(repo: Path) -> list[str]:
    output = subprocess.run(
        ['git', '-C', str(repo), 'ls-files', '-z'],
        check=True,
        stdout=subprocess.PIPE,
    ).stdout
    return [item.decode('utf-8') for item in output.split(b'\0') if item]


def is_candidate(path: str, extensions: set[str], excludes: Iterable[str]) -> bool:
    normalized = path.replace('\\', '/')
    if any(part in normalized for part in excludes):
        return False
    return Path(normalized).suffix.lower() in extensions


def line_count(path: Path) -> int:
    try:
        with path.open('r', encoding='utf-8') as handle:
            return sum(1 for _ in handle)
    except (UnicodeDecodeError, OSError):
        return 0


def collect_churn(repo: Path, revision_range: str) -> tuple[dict[str, int], dict[str, int]]:
    output = run_git(repo, 'log', '--numstat', '--format=commit:%H', revision_range)
    churn: dict[str, int] = defaultdict(int)
    commits: dict[str, set[str]] = defaultdict(set)
    current_commit = ''

    for raw_line in output.splitlines():
        if raw_line.startswith('commit:'):
            current_commit = raw_line.removeprefix('commit:')
            continue
        parts = raw_line.split('\t', 2)
        if len(parts) != 3 or not parts[0].isdigit() or not parts[1].isdigit():
            continue
        additions, deletions, path = int(parts[0]), int(parts[1]), parts[2]
        churn[path] += additions + deletions
        if current_commit:
            commits[path].add(current_commit)

    return dict(churn), {path: len(values) for path, values in commits.items()}


def score(lines: int, churn: int, touching_commits: int) -> float:
    if lines <= 0 or churn <= 0:
        return 0.0
    return math.log1p(lines) * math.log1p(churn) * math.sqrt(max(1, touching_commits))


def rank_rows(rows: list[dict[str, int | str]]) -> list[dict[str, int | float | str]]:
    ranked: list[dict[str, int | float | str]] = []
    for row in rows:
        item = dict(row)
        item['score'] = round(score(int(row['lines']), int(row['churn']), int(row['touching_commits'])), 4)
        ranked.append(item)
    return sorted(ranked, key=lambda row: (-float(row['score']), -int(row['churn']), str(row['path'])))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo', type=Path, default=Path('.'))
    parser.add_argument('--base', default='v0.2.0', help='Git base ref; range is BASE..HEAD')
    parser.add_argument('--top', type=int, default=20)
    parser.add_argument('--json-out', type=Path)
    parser.add_argument('--csv-out', type=Path)
    parser.add_argument('--extensions', default=','.join(sorted(DEFAULT_EXTENSIONS)))
    args = parser.parse_args()

    repo = args.repo.resolve()
    extensions = {item.strip().lower() for item in args.extensions.split(',') if item.strip()}
    revision_range = f'{args.base}..HEAD'
    churn, commits = collect_churn(repo, revision_range)

    rows = []
    for relative in tracked_files(repo):
        if not is_candidate(relative, extensions, DEFAULT_EXCLUDES):
            continue
        lines = line_count(repo / relative)
        rows.append({
            'path': relative,
            'lines': lines,
            'churn': churn.get(relative, 0),
            'touching_commits': commits.get(relative, 0),
        })

    ranked = rank_rows(rows)[: args.top]
    payload = {
        'schema_version': 1,
        'base': args.base,
        'revision_range': revision_range,
        'formula': 'ln(1+lines) * ln(1+churn) * sqrt(max(1,touching_commits))',
        'candidates': ranked,
    }

    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(payload, indent=2) + '\n', encoding='utf-8')
    else:
        print(json.dumps(payload, indent=2))

    if args.csv_out:
        args.csv_out.parent.mkdir(parents=True, exist_ok=True)
        with args.csv_out.open('w', newline='', encoding='utf-8') as handle:
            writer = csv.DictWriter(handle, fieldnames=['path', 'lines', 'churn', 'touching_commits', 'score'])
            writer.writeheader()
            writer.writerows(ranked)

    return 0


if __name__ == '__main__':
    raise SystemExit(main())
