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


SCHEMA_VERSION = 2
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

# Local Git configuration must not decide the numbers in a receipt that claims to be
# reproducible. Each of these changes churn or rename detection without changing the
# repository state, so each is pinned rather than inherited:
#   core.attributesFile / core.attributesfile - a global attributes file marking a
#     source extension `binary` turns numeric churn into `-` and silently scores it 0
#   diff.renameLimit - when exceeded, Git warns on stderr and reports a rename as a
#     delete plus an add, which is exactly the lineage break this tool exists to avoid
#   diff.algorithm - histogram/patience need not produce the same added/deleted counts
#     as the default for a given blob pair
#   core.bigFileThreshold - files above it are diffed as binary and score 0 churn
GIT_DETERMINISM_OPTIONS = (
    "-c", "core.attributesFile=",
    "-c", "diff.renameLimit=0",
    "-c", "diff.algorithm=myers",
    "-c", "core.bigFileThreshold=512m",
)
# `-c` outranks every configuration source including `GIT_CONFIG_KEY_*`, so the pins
# above do not need the surrounding config scrubbed - and scrubbing it would be wrong,
# because system configuration also carries platform end-of-line settings that decide
# whether a checkout reads as clean.
#
# The pins are applied to the history and diff reads only, never to the tracked-state
# probe, for the same reason: neutralising a contributor's global attributes file
# changes how `git status` compares an end-of-line-exempted blob, which would report a
# genuinely clean checkout as dirty.
GIT_ENVIRONMENT_REMOVED = (
    "GIT_ALTERNATE_OBJECT_DIRECTORIES",
    "GIT_COMMON_DIR",
    "GIT_DIR",
    "GIT_INDEX_FILE",
    "GIT_OBJECT_DIRECTORY",
    "GIT_WORK_TREE",
)
# `$(prefix)/etc/gitattributes` is a file, so no `-c` value can reach it; only this
# variable suppresses it. `GIT_ATTR_SOURCE` (Git 2.40+) redirects the `.gitattributes`
# lookup to an arbitrary tree-ish, so it is dropped alongside. Both apply to the pinned
# reads only. There is no `GIT_ATTRIBUTES_FILE` in Git - measured on 2.45.1, setting it
# changes nothing - so it is deliberately not listed.
GIT_PINNED_ENVIRONMENT_OVERRIDES = {"GIT_ATTR_NOSYSTEM": "1"}
GIT_PINNED_ENVIRONMENT_REMOVED = ("GIT_ATTR_SOURCE",)


def _git_environment(pinned: bool) -> dict[str, str]:
    removed = set(GIT_ENVIRONMENT_REMOVED)
    if pinned:
        removed.update(GIT_PINNED_ENVIRONMENT_REMOVED)
    environment = {key: value for key, value in os.environ.items() if key not in removed}
    if pinned:
        environment.update(GIT_PINNED_ENVIRONMENT_OVERRIDES)
    return environment


class AnalysisError(RuntimeError):
    """A bounded, user-actionable analysis failure."""


def _run_git(
    repo: Path,
    *args: str,
    check: bool = True,
    timeout: int = GIT_TIMEOUT_SECONDS,
    input_data: bytes | None = None,
    pinned: bool = False,
) -> subprocess.CompletedProcess[bytes]:
    """Run one Git command. `pinned` adds the determinism policy; see its constants.

    Only the reads whose *output* the ranked numbers come from are pinned. Object reads
    (`ls-tree`, `cat-file`) are unaffected by attributes or diff configuration, and the
    tracked-state probe must see the checkout exactly as the contributor's own Git does.
    """

    options = GIT_DETERMINISM_OPTIONS if pinned else ()
    try:
        result = subprocess.run(
            ["git", "--no-replace-objects", *options, "-C", os.fspath(repo), *args],
            check=False,
            env=_git_environment(pinned),
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


def _decode_git_path(raw: bytes) -> str | None:
    """Decode one repository-relative Git path, or None when it is not valid UTF-8.

    Git already emits repository-relative paths with forward slashes, so a backslash is a
    literal filename character on POSIX and is preserved. A path that cannot be represented
    is reported as None so the caller can exclude it instead of aborting the whole report.
    """

    try:
        path = raw.decode("utf-8")
    except UnicodeDecodeError:
        return None
    pure = PurePosixPath(path)
    if not path or pure.is_absolute() or ".." in pure.parts or "\x00" in path:
        raise AnalysisError("Git returned an unsafe repository-relative path")
    return path


def resolve_repository(repo_argument: Path) -> Path:
    repo = repo_argument.expanduser().resolve()
    result = _run_git(repo, "rev-parse", "--show-toplevel")
    try:
        # Only the record terminator is removed: a repository directory may legitimately
        # end in whitespace, and stripping it would reject a valid --repo argument.
        decoded = result.stdout.decode("utf-8").removesuffix("\n").removesuffix("\r")
        reported = Path(decoded).resolve()
    except UnicodeDecodeError as error:
        raise AnalysisError("Git repository root is not valid UTF-8") from error
    if reported != repo:
        raise AnalysisError(f"--repo must be the exact Git root (Git reported {reported})")
    return repo


def repository_local_attributes_present(repo: Path) -> bool:
    """Report whether `info/attributes` exists - per-clone, uncommitted, attribute state.

    It is recorded rather than rejected. `-c` cannot override it and no environment
    variable disables it, but it is also a legitimate local housekeeping file: Taskdeck's
    own checkout uses one to mark end-of-line handling for a handful of tracked files.
    Refusing to run would make the tool unusable in the repository it exists for, so the
    receipt states the fact and the reader decides whether two runs are comparable.
    """

    result = _run_git(repo, "rev-parse", "--path-format=absolute", "--git-path", "info/attributes")
    try:
        attributes_path = Path(result.stdout.decode("utf-8").removesuffix("\n").removesuffix("\r"))
    except UnicodeDecodeError as error:
        raise AnalysisError("Git attributes path is not valid UTF-8") from error
    if not attributes_path.is_absolute():
        attributes_path = repo / attributes_path
    return attributes_path.exists()


def require_no_grafts(repo: Path) -> None:
    result = _run_git(repo, "rev-parse", "--path-format=absolute", "--git-path", "info/grafts")
    try:
        grafts_path = Path(result.stdout.decode("utf-8").strip())
    except UnicodeDecodeError as error:
        raise AnalysisError("Git graft path is not valid UTF-8") from error
    if not grafts_path.is_absolute():
        grafts_path = repo / grafts_path
    if grafts_path.exists():
        raise AnalysisError("Git graft metadata is present; remove info/grafts before ranking")


def resolve_commit(repo: Path, ref: str) -> str:
    if not ref or "\x00" in ref:
        raise AnalysisError("Git ref must be non-empty and cannot contain NUL")
    result = _run_git(repo, "rev-parse", "--verify", "--end-of-options", f"{ref}^{{commit}}")
    commit = result.stdout.decode("ascii", errors="strict").strip().lower()
    if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
        raise AnalysisError(f"Git resolved {ref!r} to an unexpected object name")
    return commit


def classify_base_ref(repo: Path, ref: str) -> str:
    """Report what kind of baseline was used, so an exploratory run is visibly exploratory.

    A tag is the only baseline shape a milestone report may use; a branch or bare commit
    is exploratory because it can move or was never released.
    """

    if ref == "HEAD":
        return "head"
    for namespace, kind in (("refs/tags/", "tag"), ("refs/heads/", "branch"), ("refs/remotes/", "remoteBranch")):
        probe = _run_git(repo, "rev-parse", "--verify", "--quiet", "--end-of-options", f"{namespace}{ref}", check=False)
        if probe.returncode == 0:
            return kind
    if re.fullmatch(r"[0-9a-fA-F]{4,64}", ref):
        return "commit"
    return "other"


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


def tracked_tree_entries(repo: Path, commit: str) -> list[tuple[str | None, str, str, str, int | None]]:
    result = _run_git(repo, "ls-tree", "-r", "-z", "--long", "--full-tree", commit, "--")
    entries: list[tuple[str | None, str, str, str, int | None]] = []
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
    pure = PurePosixPath(path)
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


CommitChange = tuple[str, str | None, int | None, int | None]
"""Status letter, rename source path, added lines, deleted lines."""


def parse_git_log_z(raw: bytes) -> list[tuple[str, dict[str, CommitChange]]]:
    """Parse `git log --format=%x01%H --raw --numstat -z`.

    Returns one `(commit, changes)` pair per commit, where `changes` is keyed by the
    destination path. Git emits the raw status records for a commit before its numstat
    records, so both sections are merged by destination path rather than by position.
    Paths that are not valid UTF-8 are dropped: they can never match a ranked candidate,
    and aborting on an unrelated historical path would make the tool unusable in an
    otherwise supported repository.
    """

    tokens = raw.split(b"\0")
    if tokens and tokens[-1] == b"":
        tokens.pop()

    commits: list[tuple[str, dict[str, CommitChange]]] = []
    statuses: dict[str, tuple[str, str | None]] | None = None
    counts: dict[str, tuple[int | None, int | None]] | None = None
    pending: list[tuple[str, dict[str, tuple[str, str | None]], dict[str, tuple[int | None, int | None]]]] = []

    index = 0
    while index < len(tokens):
        token = tokens[index].lstrip(b"\n")
        index += 1
        if not token:
            continue

        if token.startswith(b"\x01"):
            commit = token[1:].decode("ascii", errors="replace").strip().lower()
            if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
                raise AnalysisError("Git log returned an invalid commit identifier")
            statuses = {}
            counts = {}
            pending.append((commit, statuses, counts))
            continue

        if statuses is None or counts is None:
            raise AnalysisError("Git log emitted a change record before any commit header")

        if token.startswith(b":"):
            fields = token.split(b" ")
            if len(fields) < 5:
                raise AnalysisError("Git raw diff metadata was malformed")
            status = fields[-1].decode("ascii", errors="replace").upper()[:1]
            if not status:
                raise AnalysisError("Git raw diff metadata was malformed")
            path_count = 2 if status in {"C", "R"} else 1
            if index + path_count > len(tokens):
                raise AnalysisError("Git raw diff record was incomplete")
            if path_count == 2:
                old_path = _decode_git_path(tokens[index])
                new_path = _decode_git_path(tokens[index + 1])
            else:
                old_path = None
                new_path = _decode_git_path(tokens[index])
            index += path_count
            if new_path is not None:
                statuses[new_path] = (status, old_path)
            continue

        parts = token.split(b"\t", 2)
        if len(parts) != 3:
            raise AnalysisError("Git numstat output was malformed")
        added_raw, deleted_raw, path_raw = parts
        if path_raw:
            new_path = _decode_git_path(path_raw)
        else:
            if index + 1 >= len(tokens):
                raise AnalysisError("Git rename numstat output was incomplete")
            new_path = _decode_git_path(tokens[index + 1])
            index += 2

        if added_raw == b"-" and deleted_raw == b"-":
            added = deleted = None
        elif added_raw.isdigit() and deleted_raw.isdigit():
            added, deleted = int(added_raw), int(deleted_raw)
        else:
            raise AnalysisError("Git numstat output contained an invalid line count")
        if new_path is not None:
            counts[new_path] = (added, deleted)

    for commit, commit_statuses, commit_counts in pending:
        changes: dict[str, CommitChange] = {}
        for new_path, (status, old_path) in commit_statuses.items():
            added, deleted = commit_counts.get(new_path, (None, None))
            changes[new_path] = (status, old_path, added, deleted)
        commits.append((commit, changes))
    return commits


class _PathLineages:
    """Order-independent file lineages keyed by `(path, generation)`.

    Two problems drive this structure. First, `git log` linearises a DAG, so a sibling
    branch may edit the old name either before or after the branch that renames it; a
    forward-only alias map would strand the totals recorded under whichever name was
    visited first. Renames are therefore recorded as undirected unions, which makes the
    result independent of the traversal order. Second, a path can be deleted and then
    reused by an unrelated file. Every event that creates a new occupant at an already
    seen path opens a new generation, so the earlier occupant's history is not inherited.
    """

    def __init__(self) -> None:
        self._generation: dict[str, int] = {}
        self._parent: dict[tuple[str, int], tuple[str, int]] = {}

    def _find(self, key: tuple[str, int]) -> tuple[str, int]:
        root = self._parent.setdefault(key, key)
        while root != self._parent[root]:
            root = self._parent[root]
        while self._parent[key] != root:
            self._parent[key], key = root, self._parent[key]
        return root

    def _union(self, left: tuple[str, int], right: tuple[str, int]) -> None:
        left_root, right_root = self._find(left), self._find(right)
        if left_root != right_root:
            self._parent[left_root] = right_root

    def current(self, path: str) -> tuple[str, int]:
        """The lineage key for the file occupying `path` right now."""

        return (path, self._generation.setdefault(path, 0))

    def create(self, path: str) -> tuple[str, int]:
        """Open a lineage for a newly created occupant of `path`."""

        if path in self._generation:
            self._generation[path] += 1
        else:
            self._generation[path] = 0
        key = (path, self._generation[path])
        self._find(key)
        return key

    def rename(self, old_path: str, new_path: str) -> tuple[str, int]:
        """Link the file leaving `old_path` to the new occupant of `new_path`."""

        source = self.current(old_path)
        target = self.create(new_path)
        self._union(source, target)
        return target

    def root_for(self, key: tuple[str, int]) -> tuple[str, int]:
        return self._find(key)


def collect_churn(
    repo: Path,
    base_commit: str,
    head_commit: str,
    head_paths: Iterable[str],
) -> tuple[dict[str, int], dict[str, int]]:
    revision_range = f"{base_commit}..{head_commit}"
    result = _run_git(
        repo,
        "log",
        "--no-merges",
        "--reverse",
        "--topo-order",
        "--format=%x01%H",
        "--raw",
        "--numstat",
        "-z",
        "--find-renames",
        "--no-ext-diff",
        "--no-textconv",
        revision_range,
        "--",
        pinned=True,
    )

    lineages = _PathLineages()
    churn: dict[tuple[str, int], int] = defaultdict(int)
    touches: dict[tuple[str, int], set[str]] = defaultdict(set)

    # Oldest first, and each commit's paths in a fixed order, so the same DAG always
    # produces the same lineages regardless of how Git chose to linearise the branches.
    for commit, changes in parse_git_log_z(result.stdout):
        for new_path in sorted(changes):
            status, old_path, added, deleted = changes[new_path]
            if status == "R" and old_path is not None:
                key = lineages.rename(old_path, new_path)
            elif status in {"A", "C", "R"}:
                # A rename whose source path could not be decoded still puts a *new*
                # occupant at the destination. Treating it as an edit would hand the
                # previous occupant's history to an unrelated file.
                key = lineages.create(new_path)
            else:
                key = lineages.current(new_path)
            touches[key].add(commit)
            if added is not None and deleted is not None:
                churn[key] += added + deleted

    # Totals are accumulated per key and grouped only once the whole range has been read,
    # because a rename seen late must still collect what was recorded under the old name.
    grouped_churn: dict[tuple[str, int], int] = defaultdict(int)
    grouped_touches: dict[tuple[str, int], set[str]] = defaultdict(set)
    for key, amount in churn.items():
        grouped_churn[lineages.root_for(key)] += amount
    for key, commit_ids in touches.items():
        grouped_touches[lineages.root_for(key)].update(commit_ids)

    resolved_churn: dict[str, int] = {}
    resolved_touches: dict[str, int] = {}
    for relative in head_paths:
        root = lineages.root_for(lineages.current(relative))
        total_churn = grouped_churn.get(root, 0)
        total_touches = len(grouped_touches.get(root, ()))
        if total_churn:
            resolved_churn[relative] = total_churn
        if total_touches:
            resolved_touches[relative] = total_touches
    return resolved_churn, resolved_touches


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
    require_no_grafts(repo)
    local_attributes = repository_local_attributes_present(repo)
    base_commit = resolve_commit(repo, base_ref)
    base_ref_kind = classify_base_ref(repo, base_ref)
    head_commit = resolve_commit(repo, "HEAD")
    require_ancestor(repo, base_commit, head_commit)

    clean = tracked_tree_is_clean(repo)
    if not clean and not allow_dirty:
        raise AnalysisError("Tracked files are dirty; commit/stage resolution is required or pass --allow-dirty")

    git_version = _run_git(repo, "--version").stdout.decode("ascii", errors="replace").strip()
    rows: list[dict[str, int | str]] = []
    excluded_unreadable = 0
    excluded_undecodable_paths = 0
    candidate_files = 0
    eligible_entries: list[tuple[str, str]] = []
    for relative, mode, object_type, object_id, size in tracked_tree_entries(repo, head_commit):
        if relative is None:
            excluded_undecodable_paths += 1
            continue
        if not is_candidate(relative, extensions):
            continue
        candidate_files += 1
        if object_type != "blob" or not mode.startswith("100") or size is None or size > MAX_SOURCE_BYTES:
            excluded_unreadable += 1
            continue
        eligible_entries.append((relative, object_id))

    churn, touches = collect_churn(
        repo,
        base_commit,
        head_commit,
        (relative for relative, _object_id in eligible_entries),
    )
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
        "gitObjectPolicy": {
            "replacementObjects": "ignored",
            "grafts": "rejected",
            "repositoryLocalAttributes": "present" if local_attributes else "absent",
            "systemAndGlobalAttributesInDiffReads": "ignored",
            "pinnedDiffOptions": list(GIT_DETERMINISM_OPTIONS),
        },
        "lineSource": "Git blobs from headCommit",
        "trackedTreeClean": clean,
        # Only source-state provenance: it says the ranked numbers came from the exact
        # headCommit objects with no tracked working-tree drift. Milestone authority also
        # requires the baseline to be that milestone's final release tag, which this tool
        # cannot decide - see docs/analysis/refactoring/README.md.
        "sourceStateAuthoritative": clean,
        "baseRefKind": base_ref_kind,
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
            "excludedUndecodableTrackedPaths": excluded_undecodable_paths,
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
        json_out = args.json_out.expanduser().resolve() if args.json_out else None
        csv_out = args.csv_out.expanduser().resolve() if args.csv_out else None
        if json_out is not None and json_out == csv_out:
            # Writing both to one path silently replaces the JSON receipt with the CSV.
            raise AnalysisError("--json-out and --csv-out must resolve to different paths")
        report = build_report(args.repo, args.base, extensions, args.top, args.allow_dirty)
        if json_out is not None:
            write_json(json_out, report)
        else:
            print(json.dumps(report, indent=2, sort_keys=True))
        if csv_out is not None:
            write_csv(csv_out, report["candidates"])  # type: ignore[arg-type]
    except AnalysisError as error:
        print(f"refactor-ranker: {error}", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"refactor-ranker: output failed: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
