# User deactivation and assignment cleanup (GH-3000)

Last Updated: 2026-09-12

Status: implementation candidate. The local environment has no .NET SDK; the new
C# cases have not been compiled or executed locally. Hosted qualification and
independent review are required before merge.

## Contract and cause

Assignment eligibility excludes inactive users, but excluding a user from the
picker does not remove existing responsibilities. Previously user deactivation
only changed `IsActive`, leaving assignments visible on active and archived cards
and in exports. Access revocation and account erasure already had a cleanup path.

`UserService.DeactivateUserAsync` now uses that same `CardAssignmentService`
cleanup operation, not a competing query, bulk delete or background reconciler.
The service is a required dependency: an omitted optional dependency must not
silently leave deactivation half implemented. Existing production DI resolves it;
only the three explicit unit-test construction sites need updating.

## Transaction and notification boundary

1. Start the existing unit-of-work transaction and read the target user.
2. Stage detachments for that user across all boards, including archived cards
   and cards on archived boards. Keep every other assignee and every card ID,
   title, column, lifecycle state and board ownership unchanged.
3. Stage one card audit row per cleanup through the existing helper. The reason
   is `user-deactivated`, with `removedUserId` and the self-deactivating actor.
   The domain detach advances affected card versions; unrelated cards stay intact.
4. Set `IsActive=false`, save once, then commit. Domain failures map to the existing
   Result failure; unexpected failures roll back and propagate. Neither failure
   path invalidates the active-user cache or publishes assignment notifications.
5. Only after commit, invalidate the active-user cache and emit the existing
   `card.assignments` event for each affected board/card pair. This retains the
   per-card identity contract used by realtime and webhook consumers.

A post-commit channel failure must not enter the transaction rollback handler.
The production composite notifier already logs/catches channel failures. No
catch-and-ignore policy is introduced in this service. A custom throwing notifier
can still propagate an error after commit; a regression pins that this does not
attempt a fictitious rollback of durable data.

This is the same relational unit-of-work boundary used by neighboring cleanup
operations. See [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
for explicit transaction/SaveChanges semantics and provider caveats. The candidate
adds no schema, migration, direct SQL, new worker, retry loop or provider claim.

## Authority and reactivation

The existing controller only permits self-deactivation; an anonymous caller is
refused and another authenticated user cannot deactivate this account. Cleanup
is a consequence of account state, not a grant to edit those boards. The actor
passed to the helper equals the target because this route is self-scoped. A future
administrator-initiated route would need its own explicit actor contract; it must
not reuse that assumption silently.

Reactivation does not restore detached responsibility. There is no assignment
snapshot or resurrection code in `ActivateUserAsync`. The current inactive-user
HTTP middleware policy remains unchanged: this PR does not create a public
reactivation channel. The API integration test exercises the existing activation
service from a trusted test scope, separately from HTTP authorization.

## Deliberate limits and alternatives

- No transactional outbox is added to this direct deactivation path. Cleanup and
  audit are atomic; post-commit webhook enqueueing still has the existing
  commit-to-notify crash window. The proposal-only durability work in GH-3071 is
  independent and must not be cited as a guarantee for this path. Event queues
  created by the normal notifier are checked in the test, not external delivery.
- No assignment-count cap or chunked batch is introduced. Materialization scales
  with the user's assignments, as in existing account-erasure cleanup. A paged
  redesign would need to preserve the all-or-nothing account-state boundary.
- No new concurrency token is added to User, no PostgreSQL run is claimed and no
  concurrent external-writer race is simulated. Existing card concurrency and
  provider transaction semantics remain responsible for that boundary.
- No change to board membership/ownership, approval authority, subscription
  ownership, deactivation confirmation copy or historical authorship is made.
  Removing a responsibility is distinct from erasing an account or its audit trail.
- A background cleanup job was rejected for this slice because it would make
  successful deactivation temporarily retain responsibilities and introduce a
  separate failure/reconciliation state. Filtering exports alone would conceal
  stale rows rather than correct their source.

## Supplied regression coverage

Eight application cases use real domain objects and the real assignment helper
with mocked repositories: active/archived cleanup across boards; other-assignee
preservation; missing user; detach/save/commit failures; unexpected-save rollback;
reactivation permanence; and no rollback on a post-commit notifier failure.
Mock rollback does not prove persistent rollback; the API suite covers that.

Four API cases use the existing worker-disabled factory and real SQLite:

- Self-deactivation cleans active and archived cards, including an archived board,
  preserves another assignee and an unrelated card, invalidates active-user access,
  and exposes the resulting assignments through card detail and board export.
  Audit reasons/actors and queued webhook event/card IDs are asserted. Reactivation
  through the trusted service produces no restored assignments or new cleanup rows.
- A SaveChanges interceptor throws *after the relational save but before commit*.
  A separate scope must see the original active user, assignments and card versions,
  with no cleanup audit or queued delivery. The fixture requires its injection to
  fire and records that an explicit transaction existed.
- Anonymous and foreign-account requests preserve all assignments and account state.
- The first notification callback reads through a separate scope and must already
  observe all committed cleanup/audits, not just the card named by that event.

These are controlled integration fixtures, not a process-kill, live SignalR client,
external HTTP webhook delivery, physical-device or screen-reader acceptance run.
The export assertion reads the typed payload inside the existing portable envelope
without pinning the unrelated estimate/relation feature's version number.

## Verification and reproduction

Original UserService and UserServiceTests blobs in the upload matched pinned main
`44d041ca7d819d450976fc974e14623718f2f467` before editing. Remote history is
parented on that actual commit. Local checks use the supplied older snapshot, not
an asserted complete checkout of the newer main. Other feature files are untouched.

The following commands are the exact-head qualification targets:

```sh
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release --filter 'FullyQualifiedName~UserServiceTests|FullyQualifiedName~UserDeactivationAssignmentTests' -m:1
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release --filter 'FullyQualifiedName~UserDeactivationAssignmentsApiTests' -m:1
dotnet test backend/Taskdeck.sln -c Release -m:1
```

The local attempt fails to start (`dotnet: command not found`). No local C# red/green,
compilation, database or browser verdict is claimed. Documentation governance,
GitHub-operations governance, relative-link and whitespace checks are available
locally; exact-head hosted results are recorded in the PR receipt when observed.

No canonical STATUS/MASTERPLAN changes or human checkbox completions are made for
this candidate. Existing physical-device/keyboard, screen-reader, translation,
provider, release/hosting and CI-control decisions in OUTSTANDING_TASKS.md remain
unchanged. The implementation is independent of the permission/archive-editor,
typed-relation and deferred-proposal webhook delivery stacks.
