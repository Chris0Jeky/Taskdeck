# Board-access read and session ownership

Status: integrated source PR #3330 on current main, 2026-09-22. Preserved source:
`112585b7227e55c9083e263a0d49d7faf9673f58`; integration base:
`346b3c756875f131fdf83191d218427e50277021`.

## Corrected behavior

A board-access read could settle after a confirmed grant, update or revoke and replace newer
client state. Same-board reads were last-response-wins, one shared loading Boolean could clear
while other boards still loaded, and pending operations retained permission to publish after
account replacement. The source history also corrects token refresh that could strand the
unchanged Board Access route with an unresolved first read.

- One read owner exists per board; unrelated boards remain concurrent.
- A successful mutation advances that board's generation and retires older reads.
- User identity, authentication or demo-session replacement advances the epoch, clears cached
  access, retires operations and resets loading/error.
- Token-only rotation preserves settled caches and retires old-token settlement. An active
  explicit refresh is restarted, including when the cache is populated or empty. Reads that
  opt out of refresh revalidation retry only while their board has no cache entry.
- A successful mutation retired by same-user token rotation triggers an authoritative read
  under the replacement credential. Grant, update and revoke transport is never replayed.
- Logout and same-user re-login retire the previous lifecycle's mutation reconciliation.
- Current operation tokens aggregate loading; an old finalizer cannot clear a newer owner.

Server authorization remains authoritative. This corrects client cache behavior and does not
claim a server-side authorization bypass. Same-entry mutation serialization remains in stacked
PR #3335, which requires its own integration after this parent lands.

## Evidence and remaining gates

The real-Pinia ownership suites cover read-versus-mutation races, reverse reads, independent
loading, account replacement, stale failures, token rotation, explicit refresh retry and
post-write reconciliation. Existing demo and Board Access view tests cover their callers.
The session-establishment changes now on main are part of the integration qualification.

Source commits and prior review history are preserved. Current-base local checks, a bounded
independent integration review and exact-head hosted Required CI are recorded on the integration
PR; this note alone grants no merge qualification. Human actions remain in
[OUTSTANDING_TASKS.md](../../OUTSTANDING_TASKS.md).