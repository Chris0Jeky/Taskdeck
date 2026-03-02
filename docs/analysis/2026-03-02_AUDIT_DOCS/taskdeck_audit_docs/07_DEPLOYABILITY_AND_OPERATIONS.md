# Deployability and Operations

Score: **8 / 10**  
(Docker baseline is well thought out, environment variable strategy exists, and health endpoints + telemetry hooks are present. The main gaps are “secure-by-default” TLS and horizontal scale support.)

## 1) What deployment looks like today

### Reference stack
- `deploy/docker-compose.yml` defines:
  - nginx reverse proxy (publishes port 8080 by default)
  - backend API container
  - frontend static container

### Config management
- Uses `.env` patterns (`deploy/.env.example`)
- Backend reads settings from environment variables and configuration sections.

**Strength**
- This is a clean and common “single-node” deployment story.

## 2) Reverse proxy behavior

nginx config:
- serves frontend
- proxies `/api/` to backend
- proxies `/hubs/boardHub` for websockets (SignalR)

Also sets:
- `X-Forwarded-For`, `X-Forwarded-Proto`, `Host`

**Important nuance**
The backend will **not** trust forwarded headers unless `ForwardedHeaders:KnownNetworks/KnownProxies` is configured.
This is the correct security posture, but it must be operationally understood — otherwise all clients appear to come from the proxy IP, impacting rate limits.

## 3) Health checks and readiness

Backend includes a health controller with:
- basic health
- deeper checks including DB connectivity and queue depth

**Strength**
- These can be wired into orchestrators or monitoring.

**Weakness**
- Some checks compute queue depth via list enumeration rather than COUNT.
  - ok for small queue
  - becomes expensive when queue grows

## 4) TLS and production hardening

The baseline proxy config is HTTP only.
For any real multi-user deployment:
- TLS must be enabled at the edge (nginx or external load balancer)
- HSTS is configured in app security headers, but HSTS only matters when TLS exists.

**Recommendation**
- Provide a “secure compose profile”:
  - TLS cert mount (Let’s Encrypt or provided cert)
  - strict TLS ciphers
  - HTTP→HTTPS redirect
  - secure cookies if you ever move auth to cookies

## 5) Upload limits mismatch

- nginx: `client_max_body_size 10m`
- backend DB import default limit: 50 MB (`DatabaseExportImportSettings`)

**Impact**
- large imports will fail at the proxy even if backend allows them.

**Fix**
- Align limits, and document which layer is authoritative.

## 6) Observability / ops instrumentation

Backend has:
- correlation IDs middleware
- OpenTelemetry scaffolding (metrics + tracing)
- worker metrics and health endpoints

**What’s missing**
- pre-built dashboards / alert rules
- explicit SLO targets (availability, latency, queue backlog)
- log retention / rotation strategy for persistent logs stored in DB

## 7) Operational risks

### Running multiple instances
If you run multiple API containers against the same SQLite file:
- file locking and corruption risks
- duplicate worker processing risks

If you run multiple API instances with separate SQLite volumes:
- you no longer have a single shared system; data splits.

**Recommendation:** explicitly document “single instance only” for this stack unless/until DB is upgraded.

## 8) Practical ops improvements

### Near-term
- Add a `docker compose --profile secure` variant with TLS.
- Add a “backup script” for the SQLite DB volume:
  - consistent snapshot
  - optional encryption at rest
- Add “log retention” / “vacuum” maintenance job guidance.
- Provide a minimal Grafana dashboard JSON if using OTel exporters.

### Mid-term
- Split worker host from API host (separate container).
- Move from SQLite to Postgres if multi-user + scale is a goal.
