# Feature Cost Hotspot Registry

Last Updated: 2026-04-09
Issue: `#104` OPS-12 Cloud cost observability and budget-guardrail automation
Parent: `docs/ops/CLOUD_COST_OBSERVABILITY.md`

---

## Purpose

Track features with high-variance or superlinear cost scaling. Each entry documents the cost driver, estimated cost range, scaling behavior, mitigation levers, and action owner. This registry is reviewed during the monthly cost review (see `CLOUD_COST_OBSERVABILITY.md`).

---

## Hotspot Entry Format

Each hotspot follows this structure:

- **Feature**: Name and brief description
- **Cost dimension**: Which billing category is affected
- **Estimated cost range**: Low/high monthly estimate for the expected user base
- **Scaling behavior**: How cost grows relative to users/usage
- **Current guardrails**: What controls already exist
- **Mitigation levers**: Actions available to reduce cost
- **Action owner**: Who is responsible for monitoring and mitigation
- **Risk level**: Low / Medium / High / Critical

---

## Hotspot 1: LLM API Usage (Chat and Capture Triage)

| Attribute | Detail |
|---|---|
| Feature | Automation Chat (`ChatService`), capture triage (`LlmQueueToProposalWorker`), tool-calling orchestrator |
| Cost dimension | LLM API (OpenAI / Gemini) |
| Estimated cost range | $5-50/month (10-50 users, light chat) to $200-500/month (100+ users, heavy tool-calling) |
| Scaling behavior | **Superlinear** — each chat message may trigger 1-5 tool-calling rounds, each round is a full API call with growing context window. A single complex conversation can cost 5-10x a simple one. Capture triage adds per-item LLM cost. |
| Current guardrails | Per-user rate limit: 60 req/hr. Per-user token limit: 100K tokens/day. Global budget ceiling config (`LlmQuota:GlobalBudgetCeilingTokens`). Tool-calling loop cap: 5 rounds, 60s timeout. Tool result truncation: 8KB max. Kill-switch (global/surface/per-user). Mock provider default (zero cost). |
| Mitigation levers | 1. Reduce `LlmToolCalling:MaxRounds` (default 5 → 3). 2. Lower per-user token daily limit. 3. Switch high-volume users to Mock provider. 4. Activate surface-level kill-switch for Chat or CaptureTriage. 5. Reduce context window size (`BoardContextBuilder` budget). 6. Switch from GPT-4o-mini to a cheaper model. 7. Enable clarification detection to reduce wasted rounds (`ClarificationDetector`). |
| Action owner | Product/backend lead |
| Risk level | **High** — highest variance cost component with no natural ceiling per conversation |

### Per-Request Cost Estimates (as of 2026-04)

| Scenario | Input tokens | Output tokens | Estimated cost (GPT-4o-mini) |
|---|---|---|---|
| Simple chat (no tools) | ~500 | ~200 | ~$0.00020 |
| Chat with 1 read tool | ~1,200 | ~400 | ~$0.00042 |
| Chat with 3 tool rounds | ~3,000 | ~800 | ~$0.00093 |
| Chat with 5 tool rounds (max) | ~5,500 | ~1,200 | ~$0.00155 |
| Capture triage (per item) | ~300 | ~150 | ~$0.00014 |

These estimates assume GPT-4o-mini pricing ($0.15/1M input, $0.60/1M output). Gemini 2.5 Flash has similar pricing. Actual costs depend on conversation length, board context size, and tool result sizes.

### Monthly Projections

| Usage level | Users | Messages/user/day | Tool rounds/msg | Monthly LLM cost |
|---|---|---|---|---|
| Light | 10 | 5 | 1.5 avg | ~$8 |
| Moderate | 50 | 10 | 2.0 avg | ~$85 |
| Heavy | 100 | 15 | 2.5 avg | ~$350 |
| Peak (with triage) | 100 | 15 + 20 triage | 2.5 avg | ~$430 |

---

## Hotspot 2: Logging and Telemetry Volume

| Attribute | Detail |
|---|---|
| Feature | OpenTelemetry traces/metrics, application logs, request correlation |
| Cost dimension | Logging / telemetry (CloudWatch, Grafana Cloud, or OTLP backend) |
| Estimated cost range | $5-30/month (structured, sampled) to $100-300/month (verbose, unsampled) |
| Scaling behavior | **Linear to superlinear** — log volume scales with request count. Verbose logging (DEBUG level) or high-cardinality trace attributes can cause 10-50x volume increase. Tool-calling conversations generate multiple log entries per round. |
| Current guardrails | Configurable log level. Security logging redaction baseline (sanitized exceptions, generic error messages). Configurable OTLP exporter. Metric export interval configurable. |
| Mitigation levers | 1. Set log level to `Warning` or `Error` in production. 2. Enable trace sampling (e.g., 10% of requests). 3. Reduce metric export interval. 4. Reduce `MetricExportIntervalSeconds`. 5. Set CloudWatch log retention to 14-30 days (not indefinite). 6. Exclude health-check endpoints from trace collection. 7. Cap log line length for tool-call results. |
| Action owner | Infrastructure lead |
| Risk level | **Medium** — predictable at low volume but can spike with verbose config or traffic surges |

### Retention Policy Recommendations

| Log type | Retention | Rationale |
|---|---|---|
| Application logs (INFO+) | 30 days | Sufficient for operational debugging |
| Application logs (DEBUG) | 7 days | Only enabled during active investigation |
| Trace data | 14 days | Covers typical incident investigation window |
| Metrics | 90 days | Supports monthly trend analysis |
| Audit trail (application-level) | Indefinite (in SQLite) | Compliance and provenance requirements |

---

## Hotspot 3: Database Storage Growth (SQLite / EBS)

| Attribute | Detail |
|---|---|
| Feature | SQLite database (boards, cards, audit trail, chat history, proposals, notifications) |
| Cost dimension | Storage (EBS volume) |
| Estimated cost range | $5-15/month (20-50 GB gp3 EBS) |
| Scaling behavior | **Sublinear initially, linear long-term** — audit trail and chat history grow with every operation. Without archival, database size grows indefinitely. SQLite VACUUM can reclaim space from deletions. |
| Current guardrails | S3 backup with 90-day noncurrent version expiry. EBS destroy protection on staging/prod. Account deletion anonymizes PII but does not reclaim space. |
| Mitigation levers | 1. Implement periodic SQLite VACUUM (reclaim deleted space). 2. Archive old audit trail entries to cold storage (S3 Glacier). 3. Set chat history retention limit (e.g., 90 days). 4. Compress old export artifacts. 5. Monitor EBS usage and resize proactively. 6. Enable WAL checkpointing to control WAL file growth. |
| Action owner | Infrastructure lead |
| Risk level | **Low** — predictable growth, but uncapped audit trail could become significant over years |

### Growth Estimates

| Data type | Estimated size per record | Records/user/month | 100 users, 12 months |
|---|---|---|---|
| Cards | ~2 KB | 50 | ~120 MB |
| Audit entries | ~500 bytes | 200 | ~120 MB |
| Chat messages | ~1 KB | 150 | ~180 MB |
| Proposals | ~1 KB | 30 | ~36 MB |
| Notifications | ~500 bytes | 100 | ~60 MB |
| **Total estimate** | | | **~516 MB** |

SQLite overhead and indexes add approximately 30-50%, bringing the estimated 12-month database size for 100 users to approximately 700 MB - 1 GB.

---

## Hotspot 4: SignalR Connection Overhead

| Attribute | Detail |
|---|---|
| Feature | SignalR WebSocket connections for realtime board collaboration |
| Cost dimension | Compute (memory per connection), network (WebSocket frames) |
| Estimated cost range | Negligible at current scale ($0-5/month additional compute) |
| Scaling behavior | **Linear** — each connected user maintains one persistent WebSocket. Memory: ~50-100 KB per connection. Network: minimal for idle connections, increases with board mutation frequency. |
| Current guardrails | Single-node in-process SignalR (no external backplane). Board-scoped subscription authorization. Polling fallback when WebSocket unavailable. |
| Mitigation levers | 1. Implement idle connection timeout (disconnect after N minutes of inactivity). 2. Batch board mutation events (debounce rapid-fire updates). 3. Move to Azure SignalR Service or Redis backplane for scale-out (cost shifts from compute to managed service). 4. Rate-limit SignalR event frequency per board. |
| Action owner | Backend lead |
| Risk level | **Low** — negligible cost at single-node scale; becomes relevant at 500+ concurrent connections |

---

## Hotspot 5: CI/CD Pipeline and Artifact Storage

| Attribute | Detail |
|---|---|
| Feature | GitHub Actions CI (`ci-required.yml`, `ci-nightly.yml`, `ci-extended.yml`), Docker image builds |
| Cost dimension | CI/CD (GitHub Actions minutes, container registry) |
| Estimated cost range | $0/month (free tier, public repo) to $20-50/month (private repo, heavy CI) |
| Scaling behavior | **Step function** — cost jumps when exceeding free-tier minutes (2,000 min/month for free, 3,000 for Pro). Docker image storage grows with image count and tag retention. |
| Current guardrails | CI-required is the PR gate (lightweight). CI-extended auto-triggers on infrastructure changes. CI-nightly runs extended checks. |
| Mitigation levers | 1. Prune old Docker images (keep last N tags). 2. Use GitHub Actions caching for dependency restore. 3. Reduce nightly CI frequency if cost is a concern. 4. Use smaller runners for doc-only PRs. 5. Set container registry retention policies. |
| Action owner | DevOps lead |
| Risk level | **Low** — predictable and within free-tier for most open-source projects |

---

## Hotspot 6: MCP HTTP Transport and API Key Usage

| Attribute | Detail |
|---|---|
| Feature | MCP HTTP endpoint (`/mcp`), API key authentication, external tool integrations |
| Cost dimension | Compute (request processing), LLM API (if MCP tools trigger LLM calls) |
| Estimated cost range | $0-10/month (direct compute cost negligible); LLM cost depends on tool usage patterns |
| Scaling behavior | **Linear with external integration frequency** — each MCP tool call is an HTTP request. Write tools that produce proposals may trigger LLM downstream. Rate limited at 60 req/60s per API key. |
| Current guardrails | API key rate limiting (60 req/60s). Write tools produce proposals (no direct board mutation). `approve_proposal` intentionally excluded from MCP. |
| Mitigation levers | 1. Reduce per-key rate limit. 2. Revoke unused API keys. 3. Disable MCP HTTP transport when not needed. 4. Audit API key usage patterns monthly. |
| Action owner | Product/backend lead |
| Risk level | **Low** — rate-limited and proposal-gated; indirect LLM cost is covered by Hotspot 1 |

---

## Review Schedule

This registry is reviewed during the monthly cost review (first working day of each month).

Updates required when:
- A new feature with potential cost impact is shipped
- Actual costs significantly deviate from estimates (>50% delta)
- Mitigation levers are exercised (document what was changed and the effect)
- New cost dimensions are identified (e.g., DNS, CDN, managed database)

---

## References

- Cloud cost observability framework: `docs/ops/CLOUD_COST_OBSERVABILITY.md`
- Budget breach runbook: `docs/ops/BUDGET_BREACH_RUNBOOK.md`
- LLM tool-calling cost model: `docs/spikes/SPIKE_618_COMPLETED.md`
- Managed-key usage policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md`
- Managed-key incident runbook: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md`
- Observability baseline: `docs/ops/OBSERVABILITY_BASELINE.md`
- Terraform deployment baseline: `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`
