
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

FORBIDDEN_FRAGMENTS = (
    "text", "prompt", "quote", "transcript", "filename", "file_name",
    "url", "message", "description", "title", "content", "sourcebytes",
    "speakername", "speaker_name"
)


def audit_keys(value: Any, path: str = "$") -> list[str]:
    findings: list[str] = []
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = key.replace("-", "_").lower()
            if any(fragment in normalized for fragment in FORBIDDEN_FRAGMENTS):
                findings.append(f"{path}.{key}: forbidden metric field")
            findings.extend(audit_keys(child, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            findings.extend(audit_keys(child, f"{path}[{index}]"))
    return findings


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Audit a metric fact/report JSON for content-bearing fields")
    parser.add_argument("path")
    args = parser.parse_args()
    value = json.loads(Path(args.path).read_text(encoding="utf-8"))
    findings = audit_keys(value)
    if findings:
        print("\n".join(findings))
        return 1
    print("privacy audit: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
