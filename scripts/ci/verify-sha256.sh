#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: verify-sha256.sh <expected-sha256> <artifact-path>" >&2
  exit 2
fi

expected_sha256="$1"
artifact_path="$2"

if [[ ! "${expected_sha256}" =~ ^[0-9a-fA-F]{64}$ ]]; then
  echo "expected SHA-256 must contain exactly 64 hexadecimal characters" >&2
  exit 2
fi

if [ ! -f "${artifact_path}" ] || [ -L "${artifact_path}" ]; then
  echo "artifact must be a regular file and not a symbolic link: ${artifact_path}" >&2
  exit 2
fi

printf '%s  %s\n' "${expected_sha256,,}" "${artifact_path}" \
  | sha256sum --check --strict -
