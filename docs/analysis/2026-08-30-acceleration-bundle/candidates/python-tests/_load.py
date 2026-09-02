# ADAPTED FOR THE ARCHIVE (2026-09-02, tracker #2348 follow-up). This is the one file in this
# archive that is NOT verbatim: the bundle's tests address modules by the bundle's own directory
# names (`03_IMPLEMENTATION_CANDIDATES/...`, `04_TESTING/test-vectors/...`), which do not exist
# here. `load()` and `vector()` translate those bundle-relative paths to the archive layout
# (`candidates/...`, `testing/test-vectors/...`) so the suite runs in place. No test module and no
# candidate module was edited. Run from the repository root:
#
#   py -3 -B -m unittest discover -s docs/analysis/2026-08-30-acceleration-bundle/candidates/python-tests -p "test_*.py"
#
# Known Windows failure: `test_absolute_manifest_path_is_rejected` fails because
# `Path('/etc/passwd').is_absolute()` is False under Windows path semantics — a real portability
# defect in the `backup_manifest.py` candidate, recorded in RECONCILIATION.md, not a harness bug.

from __future__ import annotations

import importlib.util
from pathlib import Path
from types import ModuleType

ROOT = Path(__file__).resolve().parents[2]

# Bundle directory -> archive directory. Longest prefix wins.
_LAYOUT = {
    "03_IMPLEMENTATION_CANDIDATES/dotnet/": "candidates/dotnet/",
    "03_IMPLEMENTATION_CANDIDATES/ops/": "candidates/ops/",
    "03_IMPLEMENTATION_CANDIDATES/python/": "candidates/python/",
    "03_IMPLEMENTATION_CANDIDATES/smart-ci/": "candidates/smart-ci/",
    "03_IMPLEMENTATION_CANDIDATES/sql/": "candidates/sql/",
    "04_TESTING/test-vectors/": "testing/test-vectors/",
    "04_TESTING/": "testing/",
    "02_ARCHITECTURE/": "architecture/",
    "05_DIAGRAMS/": "diagrams/",
    "08_DOCS_DRAFTS/": "docs-drafts/",
}


def resolve(relative_path: str) -> Path:
    """Translate a bundle-relative path to its archived location."""
    normalized = relative_path.replace("\\", "/")
    for bundle_prefix in sorted(_LAYOUT, key=len, reverse=True):
        if normalized.startswith(bundle_prefix):
            normalized = _LAYOUT[bundle_prefix] + normalized[len(bundle_prefix):]
            break
    path = ROOT / normalized
    if not path.exists():
        raise FileNotFoundError(f"{relative_path} is not in this archive (looked at {path})")
    return path


def vector(relative_path: str) -> Path:
    """Path to an archived test vector, addressed by its bundle-relative path."""
    return resolve(relative_path)


def load(relative_path: str, module_name: str) -> ModuleType:
    path = resolve(relative_path)
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
