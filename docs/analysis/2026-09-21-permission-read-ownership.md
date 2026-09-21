# Board-access read and session ownership

Status: draft PR #3330, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

A board-access read could settle after a confirmed grant, update, or revoke and replace that newer client state. Same-board reads were last-response-wins, one shared loading Boolean could clear while other boards still loaded, and pending reads or mutations retained permission to publish after account replacement.

The initial lifecycle correction then treated every token refresh as a full cache reset. A successful same-user session extension could blank the unchanged Board Access route even though its board selection and authorization identity had not changed.

## Contract

- One read owner exists per board; unrelated boards remain concurrent.
- A successful mutation advances that board's generation and retires older reads.
- User identity, authentication, or demo-session replacement advances the epoch, clears cached access, retires operations, and resets loading/error.
- Token-only rotation advances the same operation epoch and clears transient loading/error ownership, but preserves the loaded board-access cache for the unchanged user and route.
- Success, failure, toast, and cache writes require the initiating lifecycle epoch.
- Loading is derived from current operation tokens, not whichever call settles.
- A stale call still resolves or rejects to its caller; it loses only permission to alter current UI state.

Server authorization remains authoritative. This corrects truthful client cache behavior and does not claim a server-side authorization bypass. Same-entry mutation serialization remains in stacked PR #3335.

## Test-first evidence

The original ownership suite covers read-versus-grant/update/revoke races, reverse reads, independent loading, same-user logout/login, replacement-session mutation settlement, stale failures, and stable-ID grant deduplication.

Review-regression head `6bbf8d04139bef2dca91d290b910e1c07a8e76aa` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build, and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,161 tests, exactly 2 failures, 0 errors**; both failures were the new token-refresh preservation cases:

1. preserve loaded access while suppressing an old-token read;
2. preserve loaded access while suppressing an old-token mutation failure.

No unrelated frontend test failed.

## Remaining gates

The production correction splits token-only operation invalidation from full identity reset. Exact-head canonical tests, complete Required CI, Extended, Self-Test, and repeat independent review remain required. The stacked mutation-order PR must later be reconciled to this corrected parent and requalified.

No merge, release, or deployment qualification is claimed.
