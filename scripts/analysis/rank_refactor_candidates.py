#!/usr/bin/env python3
"""Rank current source files by explainable size, churn, and touch frequency."""

from __future__ import annotations

import argparse
import csv
import json
import math
import os
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path, PurePosixPath
from typing import Iterable, Sequence


SCHEMA_VERSION = 1
FORMULA = "ln(1+lines) * ln(1+churn) * sqrt(max(1,touchingCommits))"
DEFAULT_EXTENSIONS = frozenset(
    {".cjs", ".cs", ".css", ".js", ".mjs", ".ps1", ".py", ".scss", ".sh", ".ts", ".tsx", ".vue"}
)
EXCLUDED_DIRECTORY_SEGMENTS = frozenset(
    {
        ".git",
        ".worktrees",
        "bin",
        "build",
        "coverage",
        "dist",
        "generated",
        "migrations",
        "node_modules",
        "obj",
        "packages",
        "vendor",
    }
)
EXCLUDED_FILE_NAMES = frozenset(
    {
        "bun.lockb",
        "composer.lock",
        "package-lock.json",
        "packages.lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
    }
)
EXCLUDED_FILE_SUFFIXES = (
    ".designer.cs",
    ".generated.cs",
    ".g.cs",
    ".min.css",
    ".min.js",
    "modelsnapshot.cs",
)
MAX_SOURCE_BYTES = 10 * 1024 * 1024
GIT_TIMEOUT_SECONDS = 120
SAFE_EXTENSION = re.compile(r"^\.[a-z0-9]+$")


class AnalysisError(RuntimeError):
    """A bounded, user-actionable analysis failure."""


def _run_git(
    repo: Path,
    *args: str,
    check: bool = True,
    timeout: int = GIT_TIMEOUT_SECONDS,
    input_data: bytes | None = None,
) -> subprocess.CompletedProcess[bytes]:
    try:
        result = subprocess.run(
            ["git", "-C", os.fspath(repo), *args],
            check=False,
            input=input_data,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout,
        )
    except FileNotFoundError as error:
        raise AnalysisError("Git is not available on PATH") from error
    except subprocess.TimeoutExpired as error:
        raise AnalysisError(f"Git command timed out after {timeout} seconds: {' '.join(args[:2])}") from error

    if check and result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip().splitlines()
        reason = detail[-1] if detail else f"exit code {result.returncode}"
        raise AnalysisError(f"Git command failed ({' '.join(args[:2])}): {reason}")
    return result


def _decode_git_path(raw: bytes) -> str:
    try:
        path = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise AnalysisError("Git returned a path that is not valid UTF-8") from error
    normalized = path.replace("\\", "/")
    pure = PurePosixPath(normalized)
    if not normalized or pure.is_absolute() or ".." in pure.parts or "\x00" in normalized:
        raise AnalysisError("Git returned an unsafe repository-relative path")
    return normalized


def resolve_repository(repo_argument: Path) -> Path:
    repo = repo_argument.expanduser().resolve()
    result = _run_git(repo, "rev-parse", "--show-toplevel")
    try:
        reported = Path(result.stdout.decode("utf-8").strip()).resolve()
    except UnicodeDecodeError as error:
        raise AnalysisError("Git repository root is not valid UTF-8") from error
    if reported != repo:
        raise AnalysisError(f"--repo must be the exact Git root (Git reported {reported})")
    return repo


def resolve_commit(repo: Path, ref: str) -> str:
    if not ref or "\x00" in ref:
        raise AnalysisError("Git ref must be non-empty and cannot contain NUL")
    result = _run_git(repo, "rev-parse", "--verify", "--end-of-options", f"{ref}^{{commit}}")
    commit = result.stdout.decode("ascii", errors="strict").strip().lower()
    if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
        raise AnalysisError(f"Git resolved {ref!r} to an unexpected object name")
    return commit


def require_ancestor(repo: Path, base_commit: str, head_commit: str) -> None:
    result = _run_git(repo, "merge-base", "--is-ancestor", base_commit, head_commit, check=False)
    if result.returncode == 1:
        raise AnalysisError("--base must resolve to an ancestor of HEAD")
    if result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise AnalysisError(f"Git could not compare --base with HEAD: {detail or result.returncode}")


def tracked_tree_is_clean(repo: Path) -> bool:
    result = _run_git(repo, "status", "--porcelain=v1", "-z", "--untracked-files=no")
    return not result.stdout


def tracked_tree_entries(repo: Path, commit: str) -> list[tuple[str, str, str, str, int | None]]:
    result = _run_git(repo, "ls-tree", "-r", "-z", "--long", "--full-tree", commit, "--")
    entries: list[tuple[str, str, str, str, int | None]] = []
    for record in result.stdout.split(b"\0"):
        if not record:
            continue
        try:
            metadata, path_raw = record.split(b"\t", 1)
        except ValueError as error:
            raise AnalysisError("Git tree output was malformed") from error
        parts = metadata.split()
        if len(parts) != 4:
            raise AnalysisError("Git tree metadata was malformed")
        mode_raw, object_type_raw, object_id_raw, size_raw = parts
        try:
            mode = mode_raw.decode("ascii")
            object_type = object_type_raw.decode("ascii")
            object_id = object_id_raw.decode("ascii").lower()
        except UnicodeDecodeError as error:
            raise AnalysisError("Git tree metadata was not ASCII") from error
        if not re.fullmatch(r"[0-9a-f]{40,64}", object_id):
            raise AnalysisError("Git tree output contained an invalid object identifier")
        if size_raw == b"-":
            size = None
        elif size_raw.isdigit():
            size = int(size_raw)
        else:
            raise AnalysisError("Git tree output contained an invalid object size")
        entries.append((_decode_git_path(path_raw), mode, object_type, object_id, size))
    return entries


def parse_extensions(raw: str) -> frozenset[str]:
    extensions: set[str] = set()
    for item in raw.split(","):
        extension = item.strip().lower()
        if not extension:
            continue
        if not extension.startswith("."):
            extension = f".{extension}"
        if not SAFE_EXTENSION.fullmatch(extension):
            raise AnalysisError(f"Invalid source extension: {item!r}")
        extensions.add(extension)
    if not extensions:
        raise AnalysisError("At least one source extension is required")
    return frozenset(extensions)


def is_candidate(path: str, extensions: frozenset[str]) -> bool:
    normalized = path.replace("\\", "/")
    pure = PurePosixPath(normalized)
    lowered_parts = tuple(part.lower() for part in pure.parts)
    if any(part in EXCLUDED_DIRECTORY_SEGMENTS for part in lowered_parts[:-1]):
        return False
    name = lowered_parts[-1]
    if name in EXCLUDED_FILE_NAMES or any(name.endswith(suffix) for suffix in EXCLUDED_FILE_SUFFIXES):
        return False
    return PurePosixPath(name).suffix.lower() in extensions


def read_blobs(repo: Path, object_ids: Iterable[str]) -> dict[str, bytes]:
    unique_ids = list(dict.fromkeys(object_ids))
    if not unique_ids:
        return {}
    request = b"".join(f"{object_id}\n".encode("ascii") for object_id in unique_ids)
    result = _run_git(repo, "cat-file", "--batch", input_data=request)
    output = result.stdout
    cursor = 0
    blobs: dict[str, bytes] = {}
    for expected_id in unique_ids:
        header_end = output.find(b"\n", cursor)
        if header_end < 0:
            raise AnalysisError("Git blob batch output was incomplete")
        header = output[cursor:header_end].split()
        cursor = header_end + 1
        if len(header) != 3:
            raise AnalysisError("Git blob batch header was malformed")
        object_id_raw, object_type_raw, size_raw = header
        if object_id_raw.decode("ascii", errors="replace").lower() != expected_id:
            raise AnalysisError("Git blob batch returned an unexpected object")
        if object_type_raw != b"blob" or not size_raw.isdigit():
            raise AnalysisError("Git blob batch returned a non-blob object")
        size = int(size_raw)
        content_end = cursor + size
        if content_end >= len(output) or output[content_end : content_end + 1] != b"\n":
            raise AnalysisError("Git blob batch content was incomplete")
        blobs[expected_id] = output[cursor:content_end]
        cursor = content_end + 1
    if cursor != len(output):
        raise AnalysisError("Git blob batch returned trailing data")
    return blobs


def count_physical_lines(data: bytes) -> int | None:
    if b"\x00" in data:
        return None
    return data.count(b"\n") + (1 if data and not data.endswith(b"\n") else 0)


def parse_numstat_z(raw: bytes) -> list[tuple[int | None, int | None, str, str | None]]:
    """Parse `git show --numstat -z`; return added, deleted, new/current path, old path."""

    tokens = raw.split(b"\0")
    if tokens and tokens[-1] == b"":
        tokens.pop()
    entries: list[tuple[int | None, int | None, str, str | None]] = []
    index = 0
    while index < len(tokens):
        header = tokens[index]
        index += 1
        parts = header.split(b"\t", 2)
        if len(parts) != 3:
            raise AnalysisError("Git numstat output was malformed")
        added_raw, deleted_raw, path_raw = parts
        if path_raw:
            old_path = None
            new_path = _decode_git_path(path_raw)
        else:
            if index + 1 >= len(tokens):
                raise AnalysisError("Git rename numstat output was incomplete")
            old_path = _decode_git_path(tokens[index])
            new_path = _decode_git_path(tokens[index + 1])
            index += 2

        if added_raw == b"-" and deleted_raw == b"-":
            added = deleted = None
        elif added_raw.isdigit() and deleted_raw.isdigit():
            added, deleted = int(added_raw), int(deleted_raw)
        else:
            raise AnalysisError("Git numstat output contained an invalid line count")
        entries.append((added, deleted, new_path, old_path))
    return entries


def collect_churn(repo: Path, base_commit: str, head_commit: str) -> tuple[dict[str, int], dict[str, int]]:
    revision_range = f"{base_commit}..{head_commit}"
    commits_raw = _run_git(repo, "rev-list", "--no-merges", revision_range).stdout
    try:
        commits = [item for item in commits_raw.decode("ascii").splitlines() if item]
    except UnicodeDecodeError as error:
        raise AnalysisError("Git returned an invalid commit identifier") from error

    churn: dict[str, int] = defaultdict(int)
    touches: dict[str, set[str]] = defaultdict(set)
    aliases: dict[str, str] = {}

    # rev-list is newest first. Mapping old names backwards preserves churn under the current path.
    for commit in commits:
        result = _run_git(
            repo,
            "show",
            "--format=",
            "--numstat",
            "-z",
            "--find-renames",
            "--no-ext-diff",
            "--no-textconv",
            commit,
            "--",
        )
        for added, deleted, new_path, old_path in parse_numstat_z(result.stdout):
            canonical = aliases.get(new_path, new_path)
            aliases[new_path] = canonical
            if old_path is not None:
                aliases[old_path] = canonical
            touches[canonical].add(commit)
            if added is not None and deleted is not None:
                churn[canonical] += added + deleted

    return dict(churn), {path: len(commit_ids) for path, commit_ids in touches.items()}


def score(lines: int, churn: int, touching_commits: int) -> float:
    if lines <= 0 or churn <= 0:
        return 0.0
    return math.log1p(lines) * math.log1p(churn) * math.sqrt(max(1, touching_commits))


def rank_rows(rows: Iterable[dict[str, int | str]]) -> list[dict[str, int | float | str]]:
    ranked: list[dict[str, int | float | str]] = []
    for row in rows:
        item: dict[str, int | float | str] = dict(row)
        item["score"] = round(
            score(int(row["lines"]), int(row["churn"]), int(row["touchingCommits"])),
            6,
        )
        ranked.append(item)
    return sorted(
        ranked,
        key=lambda item: (
            -float(item["score"]),
            -int(item["churn"]),
            -int(item["lines"]),
            str(item["path"]),
        ),
    )


def build_report(
    repo_argument: Path,
    base_ref: str,
    extensions: frozenset[str],
    top: int,
    allow_dirty: bool = False,
) -> dict[str, object]:
    if top <= 0:
        raise AnalysisError("--top must be greater than zero")
    repo = resolve_repository(repo_argument)
    base_commit = resolve_commit(repo, base_ref)
    head_commit = resolve_commit(repo, "HEAD")
    require_ancestor(repo, base_commit, head_commit)

    clean = tracked_tree_is_clean(repo)
    if not clean and not allow_dirty:
        raise AnalysisError("Tracked files are dirty; commit/stage resolution is required or pass --allow-dirty")

    churn, touches = collect_churn(repo, base_commit, head_commit)
    git_version = _run_git(repo, "--version").stdout.decode("ascii", errors="replace").strip()
    rows: list[dict[str, int | str]] = []
    excluded_unreadable = 0
    candidate_files = 0
    eligible_entries: list[tuple[str, str]] = []
    for relative, mode, object_type, object_id, size in tracked_tree_entries(repo, head_commit):
        if not is_candidate(relative, extensions):
            continue
        candidate_files += 1
        if object_type != "blob" or not mode.startswith("100") or size is None or size > MAX_SOURCE_BYTES:
            excluded_unreadable += 1
            continue
        eligible_entries.append((relative, object_id))

    blobs = read_blobs(repo, (object_id for _, object_id in eligible_entries))
    for relative, object_id in eligible_entries:
        lines = count_physical_lines(blobs[object_id])
        if lines is None:
            excluded_unreadable += 1
            continue
        rows.append(
            {
                "path": relative,
                "lines": lines,
                "churn": churn.get(relative, 0),
                "touchingCommits": touches.get(relative, 0),
            }
        )

    ranked = rank_rows(rows)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "baseRef": base_ref,
        "baseCommit": base_commit,
        "headCommit": head_commit,
        "revisionRange": f"{base_commit}..{head_commit}",
        "gitVersion": git_version,
        "lineSource": "Git blobs from headCommit",
        "trackedTreeClean": clean,
        "authoritative": clean,
        "formula": FORMULA,
        "sort": ["score desc", "churn desc", "lines desc", "path asc"],
        "extensions": sorted(extensions),
        "exclusions": {
            "directorySegments": sorted(EXCLUDED_DIRECTORY_SEGMENTS),
            "fileNames": sorted(EXCLUDED_FILE_NAMES),
            "fileSuffixes": list(EXCLUDED_FILE_SUFFIXES),
            "maxSourceBytes": MAX_SOURCE_BYTES,
            "binaryRule": "files containing NUL are excluded",
        },
        "summary": {
            "trackedCandidateFiles": candidate_files,
            "rankedFiles": len(ranked),
            "excludedUnreadableBinarySymlinkOrOversize": excluded_unreadable,
            "returnedCandidates": min(top, len(ranked)),
        },
        "candidates": ranked[:top],
    }


def write_json(path: Path, report: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def write_csv(path: Path, candidates: Sequence[dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["path", "lines", "churn", "touchingCommits", "score"])
        writer.writeheader()
        writer.writerows(candidates)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Rank current tracked source files by size, churn, and touching commits.",
    )
    parser.add_argument("--repo", type=Path, default=Path("."), help="Exact Git repository root")
    parser.add_argument("--base", required=True, help="Ancestor tag or commit; analysis range is BASE..HEAD")
    parser.add_argument("--top", type=int, default=20, help="Number of candidates to return")
    parser.add_argument("--json-out", type=Path, help="Write the JSON report instead of printing it")
    parser.add_argument("--csv-out", type=Path, help="Also write candidate rows as CSV")
    parser.add_argument(
        "--extensions",
        default=",".join(sorted(DEFAULT_EXTENSIONS)),
        help="Comma-separated source extensions",
    )
    parser.add_argument(
        "--allow-dirty",
        action="store_true",
        help="Allow tracked changes and mark the report non-authoritative",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        extensions = parse_extensions(args.extensions)
        report = build_report(args.repo, args.base, extensions, args.top, args.allow_dirty)
        if args.json_out:
            write_json(args.json_out, report)
        else:
            print(json.dumps(report, indent=2, sort_keys=True))
        if args.csv_out:
            write_csv(args.csv_out, report["candidates"])  # type: ignore[arg-type]
    except AnalysisError as error:
        print(f"refactor-ranker: {error}", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"refactor-ranker: output failed: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
