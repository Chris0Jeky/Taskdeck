# REVIVAL-13 — MCP packaging, key scopes, stdio identity and tool-hash governance (#1309)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

The MCP server is discoverable, startable from the shipped artifacts, least-privilege by key, fail-closed on identity, and honest about what it does and does not enforce. Four of five acceptance criteria are on `main`. The residual is AC5 — a real external-client proof — plus one still-open governance decision about runtime tool-hash enforcement.

## Live dependencies (verified 2026-09-02)

| AC | State on `main` | Evidence |
| --- | --- | --- |
| AC1 packaging | **shipped** | `docs/MCP_SERVER.md` and `docs/MCP_TOOLING_GUIDE.md` exist; the Windows release ZIP byte-verifies the guide plus packaged-desktop and Docker stdio examples; the archive harness starts the extracted exe and asserts exactly one uncontaminated JSON-RPC `initialize` response. PR #2171 |
| AC2 key scopes | **shipped** | `backend/src/Taskdeck.Domain/Enums/ApiKeyScope.cs` (`Read`, `Propose`, `Manage`, `Full`); API/UI/CLI issuance require an explicit non-empty mask; HTTP MCP discovery *and* invocation deny before target lookup; legacy keys migrated to `Full`. PRs #2169 + #2174 |
| AC3 hash-pinning | **decided the other way, mechanism-only** | `IsToolApprovedAsync` has exactly **one** source occurrence — its own declaration at `Taskdeck.Application/Services/McpToolDefinitionHashService.cs:45`. Zero production callers. INV-10 in `RoadmapInvariantTests.cs:437` now says so in its own doc comment and asserts hashing determinism only. PR #2156 |
| AC4 stdio identity | **shipped** | `Taskdeck.Infrastructure/Mcp/StdioUserContextProvider` fails closed on empty/malformed config, a missing or inactive configured user, zero active users and ambiguous multi-user databases; implicit fallback survives only for exactly one active local user; only successful resolution is cached. PR #2167 |
| AC5 end-to-end marketing proof | **open** | A synthetic stdio `initialize` against real Claude Code 2.1.247 is recorded; no real Claude Desktop / Cursor demo transcript, no packaged public-release proof |
| README truth-drift | **fixed** | `README.md:142` now says "Runtime tool-hash approval remains planned for REVIVAL-13; scoped HTTP key enforcement does not imply that separate approval lifecycle exists" |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `MCP-R-0-reconcile` | Tick AC1/AC2/AC4 in the body against their exact merged PRs; rewrite the Context paragraph, which still describes every defect as live | — | **Yes.** Pure issue hygiene and the highest-value single act on this issue: the body currently reads as four open security defects that are all closed |
| `MCP-R-1-hash-decision` | Record the maintainer's ruling: keep the hash service as a mechanism-only drift detector (INV-10's current honest annotation) **or** build the user-driven record/approve lifecycle and enforce it at invocation | — | **No — human-only.** PR #2156 chose the honest re-annotation as a *bounded* act, not as the issue's final disposition; the either/or is still open |
| `MCP-R-2-live-smoke` | One HTTP smoke with a least-privilege key and one stdio smoke, each proving a hostile write becomes a proposal and never a direct board mutation; redacted transcripts attached | MCP-R-0 | **Yes.** Needs a running local stack, not a release |
| `MCP-R-3-external-client` | AC5: a real Claude Desktop or Cursor session against a local instance, transcript attached | MCP-R-2 | Partly — it is a human-run demo |
| `MCP-R-4-residuals` | The retained non-blocking items: `GetUserIdAsync` conflicting with its documented nullable/non-throwing contract; identity validation being lazy on first action rather than at host startup; the generic `-32603` the client sees while actionable text goes to the log; the two `docs/TESTING_GUIDE.md` labels still saying "phantom-user fallback" | — | **Yes.** Small, independent, and each is named on the issue with a location |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Review-first write model | `Api/Mcp/WriteTools.cs`, `ProposalTools.cs` | **exists** | 5 of 6 write tools create proposals; `approve_proposal` deliberately does not exist; `dismiss_proposal` cannot touch pending. This is the differentiator AC5 has to demonstrate, not rebuild |
| Key scopes | `ApiKeyScope` flags enum + one request-scoped capability mapping | **exists** | `read` gates search/get/list/resources; `propose` gates proposal-producing board tools; `manage` gates dismiss + capture. Missing, unknown and unclassified targets are denied before lookup |
| Stdio identity | `StdioUserContextProvider` | **exists, fail-closed** | Stdio is a local *identity* transport, not an API-key transport; it retains Full capability only after fail-closed user resolution |
| Tool-definition hashing | `McpToolDefinitionHashService.ComputeDefinitionHash` (SHA-256 over name + description + input schema), `RecordToolDefinitionAsync`, `ApproveToolAsync`, `IsToolApprovedAsync` | **exists, unwired** | Registered in `Api/Extensions/ApplicationServiceRegistration.cs:115`. The record/approve/check lifecycle has no production caller — deliberately, and INV-10 asserts that absence |
| Tool/resource inventory pin | The #653/#739 inventory tests | **exists** | 42 tests pin the surface. Extend, never break |
| `create_capture` proposal bypass | intentional | **exists** | Writes inbox only, never boards; sits behind the `manage` scope |
| Runtime approval lifecycle, a UI to approve a tool definition, distribution-hash enforcement | — | **new / undecided** | AC3's other branch |

## Implementation plan

**Preflight.** Read all thirteen comments in order. The body's Context paragraph and the 2026-08-23 realignment describe a state that no longer exists; five later comments record the deliveries. Reconciling the body (MCP-R-0) before any code is the pack's advice and it is right.

**AC3 is a governance decision, not a coding task.** PR #2156 discharged the *honesty* half of the either/or: INV-10 no longer reads as enforced. What it did not do is close the question of whether Taskdeck wants runtime approval at all. Frame the options for the maintainer — mechanism-only drift detection (zero new surface, the current state) versus a record → approve → enforce lifecycle (a new persisted approval per tool definition, an operator UI, and a fail-closed invocation gate that can lock an agent out after a routine description edit) — and let them rule.

**AC5 is the last real acceptance.** Everything the marketing line claims is enforced in code and covered by tests; what is missing is a transcript a human can read. Keep the two proofs separate: the hostile-write smoke (agent-runnable, MCP-R-2) and the external-client demo (human-run, MCP-R-3).

**Do not** re-implement scopes, packaging or identity. Three merged PRs own those seams and the inventory tests pin the surface.

## Test plan

- [ ] Scope denial: a `read`-only key is denied at both discovery and invocation for every propose/manage target, before target lookup — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~McpHttpTransportApiKey"`
- [ ] Stdio identity: zero active users, multiple active users, a configured-but-inactive user, a configured-but-missing user, an empty and a zero GUID — each fails closed and none falls back
- [ ] Hostile write: an MCP client attempting a direct board mutation produces a proposal or a denial, never a board write — assert at the persistence layer, not the response
- [ ] Packaged startup: the extracted release exe answers exactly one JSON-RPC `initialize` on clean stdout (already pinned by the archive harness — re-run it, do not rewrite it)
- [ ] Inventory: the #653/#739 tool/resource pin still passes after any change
- [ ] Hash drift: a name, description or input-schema change yields a different digest (INV-10, `Taskdeck.Architecture.Tests`)
- [ ] *(only if AC3's enforcement branch is chosen)* An unapproved tool definition is refused at invocation with a stable code, and the refusal is recoverable by an operator approval
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- A description-only edit to a shipped tool invalidating every approval — the reason enforcement is a decision rather than an obvious win.
- Entrypoint or logging output contaminating stdio JSON-RPC (the Docker example already disables the HTTP healthcheck for exactly this).
- An account deactivated during an active stdio session — identity is validated lazily on first action, so a long-lived session can outlive its user.
- A migrated legacy `Full` key used against a surface that assumes explicit scope selection.
- `GetUserIdAsync` throwing where its interface contract says it returns null.
- A client that sees `-32603` and cannot tell a misconfiguration from a bug.
- Two MCP hosts (stdio and HTTP) racing the same SQLite database during a migration.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Audit note | `docs/analysis/2026-08-30-acceleration-bundle/audit-m4/HIGH_LEVERAGE_RESIDUALS.md` §"MCP residual: #1309" | The four-bullet residual shape, and specifically "gate runtime pin enforcement behind an explicit decision" — the single most accurate line the bundle wrote about this issue | Its packaging and least-privilege-configuration bullets are already shipped |
| Audit note | `.../audit-m4/TRACKER_DRIFT.md` §"#1309 MCP" | "Tool-definition hashing is implemented as a recorder/service but should not be mistaken for accepted runtime pin enforcement" | Correct, and now also true of the invariant's own annotation |

## Corrections to the bundle

1. **Bundle pack:** "Comments indicate major ACs landed incrementally." **True, and understated:** AC1, AC2 and AC4 are *merged*, with named PRs (#2171, #2169 + #2174, #2167) and exact-head CI evidence on the issue. **Consequence:** the residual is AC5 plus one decision, not "narrow the residual to distribution/live-demo proof and any explicitly approved hash-pin enforcement" — distribution itself is done.
2. **Bundle pack:** "Package/configure the MCP host for the downloadable beta with one copy-paste client setup." **True:** the Windows release ZIP requires and byte-verifies `docs/MCP_SERVER.md` plus separate packaged-desktop and Docker stdio examples, and the acceptance harness performs a synthetic stdio `initialize` against the extracted binary. **Consequence:** this bullet is closed.
3. **Bundle pack:** "Persisted scope enforcement … present." **True but ambiguous:** PR #2169 shipped *persistence only* and was explicitly behavior-neutral; enforcement arrived separately in PR #2174. **Consequence:** a reader of the pack cannot tell whether enforcement exists. It does, at both discovery and invocation, for the HTTP transport.
4. **Bundle pack:** silent on stdio's capability posture. **True:** stdio is a local identity transport and retains **Full** capability after fail-closed user resolution — it is not scope-limited. **Consequence:** any least-privilege claim in a launch or MCP doc must say "HTTP transport", or it is false for stdio.
5. **Live issue body Context paragraph:** "API keys have no scopes … hash-pinning is registered-but-never-called (#1154 — INV-10 reads as enforced but isn't) … stdio identity falls back to first-user-in-DB (`StdioUserContextProvider.cs:74`)." **True on `main`:** scopes exist and are enforced; INV-10's doc comment now states the mechanism-only reality and the invariant asserts it; the first-user fallback is gone and survives only for a single active local user. **Consequence:** the body advertises three closed security defects as live, on a public issue, for a marketed surface. MCP-R-0 is the fix.
6. **Bundle pack:** "record tool hash changes, but gate runtime pin enforcement behind an explicit decision" (from `HIGH_LEVERAGE_RESIDUALS.md`). **True and still outstanding.** **Consequence:** this is the one bundle recommendation on #1309 that should be adopted verbatim.
