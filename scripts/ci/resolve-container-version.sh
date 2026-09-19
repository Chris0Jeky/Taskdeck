#!/usr/bin/env bash
# =============================================================================
# resolve-container-version.sh — release-container version authority (#1854)
# =============================================================================
#
# Converts the real Git ref used by release-container.yml into the version
# stamped into Taskdeck binaries and OCI metadata.
#
# Tag refs must clear the same strict release-tag grammar as desktop releases.
# Build metadata is valid on the Git tag but is deliberately removed from the
# product/image version because ProductVersion.Normalize reports the equivalent
# metadata-free SemVer. Non-tag refs are manual rehearsals and receive the
# repository's established development fallback.
#
# Usage: bash scripts/ci/resolve-container-version.sh <git-ref>
# Output: normalized product version on stdout.
# Exit: 0 accepted · 1 invalid tag/version · 2 wrong invocation.
# =============================================================================
set -euo pipefail

if [ "$#" -ne 1 ]; then
    printf 'usage: resolve-container-version.sh <git-ref>\n' >&2
    exit 2
fi

ref="$1"
case "$ref" in
    refs/tags/*)
        tag="${ref#refs/tags/}"
        validated_tag="$(bash scripts/ci/validate-release-tag.sh "$tag")"
        version="${validated_tag#v}"
        version="${version%%+*}"
        ;;
    *)
        version='0.0.0-dev'
        ;;
esac

# The value reaches MSBuild and Docker metadata. Keep it a plain token even if
# the shared release grammar changes later.
if ! printf '%s' "$version" | grep -Eq '^[0-9A-Za-z][0-9A-Za-z.-]*$'; then
    printf '::error::Refusing product version %q — not a plain version token.\n' "$version" >&2
    exit 1
fi

printf '%s\n' "$version"
