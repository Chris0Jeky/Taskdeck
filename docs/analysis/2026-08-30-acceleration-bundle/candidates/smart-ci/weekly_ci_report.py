#!/usr/bin/env python3
"""Build a content-free weekly Smart CI report from ci-run.v1 receipts."""
from __future__ import annotations

import argparse
import json
import math
from collections import Counter
from pathlib import Path
from typing import Iterable


def percentile(values: list[float], p: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    rank = max(1, math.ceil(p * len(ordered)))
    return ordered[rank - 1]


def build_report(receipts: Iterable[dict]) -> str:
    docs = list(receipts)
    critical = [float(doc.get('summary', {}).get('critical_path_seconds', 0)) for doc in docs]
    aggregate_runner = sum(float(doc.get('summary', {}).get('aggregate_runner_seconds', 0)) for doc in docs)
    hosted_minutes = sum(float(doc.get('summary', {}).get('hosted_minutes', 0)) for doc in docs)
    hosted_cost = sum(float(doc.get('summary', {}).get('hosted_cost_estimate', 0)) for doc in docs)
    self_hosted_seconds = sum(float(doc.get('summary', {}).get('self_hosted_wall_seconds', 0)) for doc in docs)
    flakes = sum(1 for doc in docs if doc.get('summary', {}).get('flake_detected'))
    reruns = sum(1 for doc in docs for job in doc.get('jobs', []) if job.get('rerun'))

    sha_counts = Counter(doc.get('head_sha') for doc in docs if doc.get('head_sha'))
    duplicate_shas = sorted(sha for sha, count in sha_counts.items() if count > 1)

    lane_runs: Counter[str] = Counter()
    lane_failures: Counter[str] = Counter()
    queue_seconds: list[float] = []
    cache_hits = 0
    cache_observations = 0
    artifact_bytes = 0
    for doc in docs:
        for job in doc.get('jobs', []):
            lane = str(job.get('lane', 'unknown'))
            lane_runs[lane] += 1
            if job.get('result') not in {'success', 'skipped'}:
                lane_failures[lane] += 1
            queue_seconds.append(float(job.get('queue_seconds', 0)))
            if job.get('cache_hit') is not None:
                cache_observations += 1
                cache_hits += int(bool(job.get('cache_hit')))
            artifact_bytes += int(job.get('artifact_bytes', 0) or 0)

    lines = [
        '# Weekly Smart CI report',
        '',
        f'- Receipts: **{len(docs)}**',
        f'- PR/gate critical path P50/P95: **{percentile(critical, .50):.1f}s / {percentile(critical, .95):.1f}s**',
        f'- Aggregate runner time: **{aggregate_runner / 60:.1f} min**',
        f'- Hosted time / estimated cost: **{hosted_minutes:.1f} min / {hosted_cost:.2f}**',
        f'- Self-hosted wall time: **{self_hosted_seconds / 60:.1f} min**',
        f'- Queue P50/P95: **{percentile(queue_seconds, .50):.1f}s / {percentile(queue_seconds, .95):.1f}s**',
        f'- Flaky receipts / rerun jobs: **{flakes} / {reruns}**',
        f'- Duplicate exact-SHA qualification: **{len(duplicate_shas)} SHA(s)**',
        f'- Cache hit rate: **{(100 * cache_hits / cache_observations) if cache_observations else 0:.1f}%**',
        f'- Artifact storage emitted: **{artifact_bytes / (1024 * 1024):.1f} MiB**',
        '',
        '## Lane yield',
        '',
        '| Lane | Runs | Non-success | Failure yield |',
        '|---|---:|---:|---:|',
    ]
    for lane in sorted(lane_runs):
        runs = lane_runs[lane]
        failures = lane_failures[lane]
        lines.append(f'| {lane} | {runs} | {failures} | {(100 * failures / runs):.1f}% |')

    lines.extend(['', '## Duplicate exact-SHA receipts', ''])
    lines.extend([f'- `{sha}`' for sha in duplicate_shas] or ['- None'])
    lines.append('')
    return '\n'.join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('receipts', type=Path, nargs='+')
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()

    documents = [json.loads(path.read_text(encoding='utf-8')) for path in args.receipts]
    report = build_report(documents)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(report, encoding='utf-8')
    else:
        print(report)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
