# Taskdeck hosting compatibility

Reference-only preparation, 2026-09-10. The owner requested a Render Starter 512 MB target alongside a shared Paid Workers account. No deployment, purchase, hostname, secret, application behavior or existing deployment trigger is changed here. Existing workflows may publish on a main merge; inspect them before merging.

`manifest.json` is an inert, versioned agent-intake contract. It deliberately cannot authorize activation. Follow `AGENTS.md`, `CLAUDE.md`, the current work queue and existing skills rather than treating this folder as another agent harness.

## First implementation slice

Reconcile the current ADR-0061 host-selection ruling with the new Render preference through #1772/#1777. Record the amendment without reopening known collaborator, budget and backup decisions. Keep one application instance, one persistent SQLite disk and the existing combined API/SPA image. Do not introduce D1, horizontal scaling, public registration or SaaS claims.

The existing `deploy/render.yaml` is a reference, not a receipt that a safe service exists. Review its auto-deploy behavior, registration settings, secrets and resource size before activation. Use a reviewed immutable image or exact commit promotion; no unreviewed main-tip deployment.

## Acceptance-driven follow-ons

1. **Private perimeter.** Re-evaluate #1644 and the current #1653 disposition for the new host. An Access-protected hostname is insufficient when the origin can be reached directly. Verify Access JWT signature, issuer, audience and expiry at the origin; test missing, forged, expired and wrong-audience assertions. After custom-domain setup, verify Render's default-domain disabling. Test SignalR reconnect and MCP behavior without broad bypasses. Do not enable an application MFA path still storing plaintext TOTP secrets.
2. **Recovery.** The packaged image already includes `taskdeck-backup` and `taskdeck-restore`. For Render, integrate those existing commands into the recovery procedure and schedule a restore drill into a fresh fixture using the separately held connector key. Record evidence from the actual restored drill, including connector decryptability; health and login alone do not prove recovery. A Render cron service cannot simply attach another service's persistent disk.
3. **Capacity.** Build the production image and run representative startup, import/export, backup and two-account/realtime work with a 512 MB limit and paid providers disabled. Record peak memory, OOM/restart count, latency, image digest and fixture size. If insufficient, report a bounded optimization or an explicitly approved larger-plan alternative; never silently increase spend.
4. **Runtime lifecycle.** Continue the coordinated upgrade through #1226 before .NET 8 support ends on 2026-11-10. Keep this separate from DNS and database changes.

## Verification and rollback

Syntax: `python -m json.tool .hosting/manifest.json`.

For this documentation slice, the repository proving paths are `node scripts/check-docs-governance.mjs`, `node scripts/check-doc-links.mjs` and `git diff --check`. Application, capacity, origin-security and hosted acceptance are NOT established by JSON validation. Run the applicable existing suites in each subsequent implementation PR.

Before a real promotion, record the previous image, schema compatibility, backup, expected single-instance downtime and restore target. Do not attach an old binary to an incompatible database as a rollback shortcut. Preserve SQLite paths, service IDs, API keys, storage formats and user data when a display name is eventually changed.

Sources: [existing cloud guide](../docs/platform/CLOUD_DEPLOYMENT_GUIDE.md), [Render blueprint](../deploy/render.yaml), [Render disks](https://render.com/docs/disks), [origin JWT validation](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/).
