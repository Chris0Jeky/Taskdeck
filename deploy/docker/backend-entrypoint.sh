#!/bin/sh
# Backend container entrypoint.
#
# Why this script exists:
#   - We want the API to run as non-root (appuser UID/GID 10001) for defence in
#     depth. Dockerfile USER alone is not enough when an existing named volume
#     was created by a prior image that ran as root: the on-disk files inside
#     the volume retain root ownership and the new non-root process cannot
#     write them. That would leave /health/ready permanently failing after an
#     upgrade.
#   - To make upgrades safe, we briefly start as root, chown the mounted data
#     directory to appuser, then drop privileges via setpriv before execing
#     the .NET app. setpriv is part of util-linux which is already present in
#     mcr.microsoft.com/dotnet/aspnet:8.0 (Debian 12).
#
# If the container is launched with --user appuser (or via some other mechanism
# that strips CAP_CHOWN), the chown is best-effort and the script still execs
# the app under the current uid so we never fail closed on a misconfiguration
# that is merely restrictive.

set -eu

DATA_DIR="${TASKDECK_DATA_DIR:-/app/data}"
APP_UID="${TASKDECK_APP_UID:-10001}"
APP_GID="${TASKDECK_APP_GID:-10001}"

if [ "$(id -u)" = "0" ]; then
    # Ensure the data directory exists and is writable by the app user. This
    # covers fresh volumes and upgrades from previous root-owned volumes.
    mkdir -p "$DATA_DIR"
    chown -R "$APP_UID:$APP_GID" "$DATA_DIR" || echo "warning: chown $DATA_DIR failed; continuing" >&2
    exec setpriv --reuid="$APP_UID" --regid="$APP_GID" --clear-groups -- "$@"
fi

# Already running as non-root (e.g. operator passed --user). Just exec.
exec "$@"
