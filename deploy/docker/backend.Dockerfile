FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# global.json lives at the repo root (build context root) and pins the .NET
# SDK; it must be copied in before any dotnet command so the muxer sees the
# pin. Directory.Packages.props is inside backend/ and arrives with the COPY
# below. Mirrors deploy/Dockerfile.production.
COPY global.json ./
COPY backend/ ./backend/
RUN dotnet restore backend/src/Taskdeck.Api/Taskdeck.Api.csproj
RUN dotnet publish backend/src/Taskdeck.Api/Taskdeck.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
# Mark every container built from this image as a headless deployment. This makes the first-run
# bootstrapper REQUIRE an operator-supplied Connectors__EncryptionKey (it does not auto-generate one),
# so a container cannot silently create an ephemeral key that is lost on recreate and would make stored
# connector credentials unrecoverable. Only the self-contained desktop exe (not built from this image)
# auto-generates and persists the key locally. See ADR-0041.
ENV TASKDECK_HEADLESS=true

# Install curl for HEALTHCHECK probes. aspnet:8.0 is Debian-based and has no
# HTTP client by default. util-linux (for setpriv) is already in the base
# image so we do not install it. Keep the layer lean: update, install, clean
# apt cache in one RUN.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user (UID/GID > 10000 per hardening guidance). Created before COPY
# so we can write published assets with correct ownership in a single layer
# instead of chown-ing them post-copy (which duplicates the files across
# image layers and inflates the image size).
RUN groupadd --system --gid 10001 appuser \
    && useradd --system --uid 10001 --gid 10001 --home-dir /app --shell /usr/sbin/nologin appuser \
    && mkdir -p /app/data \
    && chown appuser:appuser /app /app/data

COPY --from=build --chown=appuser:appuser /app/publish ./

# Entrypoint handles upgrade-time volume ownership and drops to appuser via
# setpriv. We keep the container starting as root so the entrypoint can chown
# /app/data on upgrade from images that ran as root; the app itself still runs
# unprivileged. security_opt: no-new-privileges (set in compose) prevents any
# later escalation.
COPY deploy/docker/backend-entrypoint.sh /usr/local/bin/taskdeck-entrypoint
RUN chmod +x /usr/local/bin/taskdeck-entrypoint

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["/usr/local/bin/taskdeck-entrypoint", "dotnet", "Taskdeck.Api.dll"]
