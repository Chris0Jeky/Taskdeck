FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/ ./backend/
RUN dotnet restore backend/src/Taskdeck.Api/Taskdeck.Api.csproj
RUN dotnet publish backend/src/Taskdeck.Api/Taskdeck.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Install curl for HEALTHCHECK probes. aspnet:8.0 is Debian-based and has no HTTP client by default.
# Keep the layer lean: update, install, clean apt cache in one RUN.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Non-root user (UID/GID > 10000 per hardening guidance). Owns /app so the
# runtime can read published assets and write to the /app/data SQLite volume.
RUN groupadd --system --gid 10001 appuser \
    && useradd --system --uid 10001 --gid 10001 --home-dir /app --shell /usr/sbin/nologin appuser \
    && mkdir -p /app/data \
    && chown -R appuser:appuser /app

EXPOSE 8080

USER appuser

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "Taskdeck.Api.dll"]
