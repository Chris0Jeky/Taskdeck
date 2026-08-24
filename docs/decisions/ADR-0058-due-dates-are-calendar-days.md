# ADR-0058: Due Dates Are Calendar Days

- **Status**: Accepted
- **Date**: 2026-08-24
- **Deciders**: Chris0Jeky (maintainer, required behaviour in `#2028`)
- **Related**: `#2028`, `#2005`, ADR-0040 (UTC materialization for timestamp fields)

## Context

Taskdeck's due-date inputs collect a date, and Calendar groups cards by date. However, `Card.DueDate`
uses the existing `DateTimeOffset?` API and persistence shape. A date-input value written as midnight
UTC can therefore move to the preceding day when a browser west of UTC parses and formats it as an
instant. The same projection can make filters and overdue indicators disagree with the entered date.

This is different from Taskdeck timestamps such as `CreatedAt`, `UpdatedAt`, or proposal expiry.
Those values identify instants. A due date identifies the day on which a card is due.

## Decision

Taskdeck card due dates are **calendar days**, not instants.

1. The canonical due-date key is the UTC `YYYY-MM-DD` portion of `Card.DueDate`.
2. `Card.DueDate` remains `DateTimeOffset?`. The API, domain, repository, and database schema do not
   change in this compatibility step.
3. Writers backed by a date-only input serialize the selected key as midnight UTC
   (`YYYY-MM-DDT00:00:00.000Z`). Readers derive the canonical UTC key before displaying, grouping,
   sorting, filtering, or calculating overdue state. They do not project the value into the
   browser's timezone.
4. Today and Calendar clients send the caller's current local calendar date as
   `localDate=YYYY-MM-DD`. The backend compares due-date keys with that supplied day when assigning
   due/overdue buckets. Callers that omit `localDate` retain the legacy UTC-day default.
5. Existing non-midnight or offset-bearing values remain readable and canonicalize by their UTC
   date. There is no data migration and no noon-UTC heuristic.

## Consequences

- The entered due day remains stable in Board card, Edit Card, Today, Saved Views, Calendar, and
  due-date filters in negative, UTC, and positive-offset timezones.
- The existing API and persistence contract stays compatible while frontend consumers share one
  date-key implementation.
- A future schema migration to a native date type remains possible, but is not required to repair
  current behaviour.
- Timestamp fields remain instants and continue to follow ADR-0040; this decision must not be
  generalized to them.

## Alternatives Considered

**Store noon UTC.** Rejected. It only moves the failure boundary and still represents a calendar
day as an instant whose displayed date depends on timezone.

**Interpret each stored offset as the due date's timezone.** Rejected. Taskdeck has no user or card
timezone attached to the value, and new date-input writes intentionally use UTC for compatibility.

**Migrate `Card.DueDate` to a database date immediately.** Deferred. It is a larger schema and API
change than the cross-surface bug requires; the canonical-key contract makes that migration an
independent future decision.
