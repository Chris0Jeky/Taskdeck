#!/usr/bin/env bash
# =============================================================================
# validate-release-tag.sh — strict release-tag grammar gate (#1795)
# =============================================================================
#
# Release Desktop accepts a `workflow_dispatch` input that names the tag to
# build and publish. That input is attacker-influenced text which later lands in
# archive file names, artifact paths, Git refs and `gh release` arguments, so it
# must clear a strict grammar BEFORE any of those uses.
#
# The value is never interpolated into shell source: the workflow passes it
# through a step `env:` var and this script receives it as "$1".
#
# Grammar (deliberately narrow — a superset of every tag Taskdeck has shipped):
#
#   v<major>.<minor>.<patch>[-<prerelease>][+<build>]
#
#   * major/minor/patch: digits only
#   * prerelease/build:  dot-separated alphanumeric identifiers
#   * total length:      1..64 characters
#
# Everything else is refused, including: path separators, whitespace, quotes,
# `$`, backticks, `;`, `&`, `|`, newlines, leading dashes, and bare refs such as
# `main` or `refs/heads/main`. `[[ =~ ]]` matches the WHOLE argument, so an
# embedded newline cannot smuggle a valid first line past the check (which a
# line-oriented `grep` would allow).
#
# Usage:  bash scripts/ci/validate-release-tag.sh "<tag>"
# Output: the validated tag on stdout (so callers can capture it).
# Exit:   0 accepted · 1 rejected tag · 2 wrong invocation.
# =============================================================================
set -euo pipefail

readonly MAX_TAG_LENGTH=64
readonly TAG_GRAMMAR='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?(\+[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$'

if [ "$#" -ne 1 ]; then
    printf 'usage: validate-release-tag.sh <tag>\n' >&2
    exit 2
fi

tag="$1"

if [ -z "${tag}" ]; then
    printf '::error::Release tag is empty; a release tag is required.\n' >&2
    exit 1
fi

if [ "${#tag}" -gt "${MAX_TAG_LENGTH}" ]; then
    printf '::error::Release tag is %s characters; the maximum is %s.\n' "${#tag}" "${MAX_TAG_LENGTH}" >&2
    exit 1
fi

if [[ ! ${tag} =~ ${TAG_GRAMMAR} ]]; then
    # Print the offending value with %q so control characters stay visible in
    # the log instead of rewriting it.
    printf '::error::Refusing release tag %q — it does not match the release-tag grammar %s\n' \
        "${tag}" 'v<major>.<minor>.<patch>[-prerelease][+build]' >&2
    exit 1
fi

printf '%s\n' "${tag}"
