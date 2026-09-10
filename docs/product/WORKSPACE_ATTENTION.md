# Optional workspace reminders

Reminders are off by default. The Quiet insights page has an explicit account preference to enable or disable them. Every experience uses the same setting and server budget. Backend-less demos hide the control.

A reminder is a quiet link to the current board's existing questions. It appears only on a board page after a minute without text input, when the tab is visible and focused. Focus/Thinking pages, Zen presentation, editable focus and open dialogs suppress it. Typing, opening a dialog, leaving the tab, changing board/account or disabling the setting clears displayed and in-flight reminders. Tab navigation remains usable. Nothing takes keyboard focus or opens a dialog automatically.

The client checks at most once every five minutes while eligible. The server revalidates saved questions through the existing insight service without analyzing the board or calling a model. Only an available question is eligible; answered, resolved, muted, dismissed, snoozed, stale or inaccessible questions are excluded. A reminder contains board/question identifiers and a generic link, not private question text.

## Reminder hours

Optional **Reminder hours** restricts new reminders to selected weekdays and local times in a saved
IANA time zone, such as `Europe/London`. The default is unrestricted for existing opted-in accounts;
reminders themselves remain off by default. **Use this device's time zone** fills the field, and
**Save reminder hours** explicitly persists the choice across devices. Toggling reminders off/on
preserves the window. Clearing the restriction requires an explicit hours save.

The start is included and the end excluded. An overnight window belongs to each selected starting
day: Monday 22:00–02:00 includes early Tuesday. Equal start/end times and invalid zones/days are
rejected. UTC instants convert to the named zone, so daylight-saving transitions follow local time;
the repeated autumn hour remains eligible while the normal UTC spacing/budget still applies.

The server checks eligibility before looking up a question and again before claiming capacity.
Outside-window polling returns no reminder and consumes no budget. Updating a loaded window clears
displayed/in-flight reminders. A reminder already offered during an allowed window may remain as a
quiet link until normal dismissal or interaction; the schedule governs new offers. No timer wakes a
closed browser. Both account exports include the window in the existing JSON preference; erasure
removes that preference. No schema migration or device-local authorization is introduced.

## Shared budget

One conditional database update claims capacity against the user's attention revision. All boards, tabs and devices share at most two claims per UTC day and two hours between claims. Crossing midnight does not bypass spacing; toggling off/on does not reset consumed capacity. The same question cannot be claimed consecutively. Lost or discarded responses conservatively consume the claim; they never trigger automatic retry. This makes the maximum a ceiling, not a promise to display two reminders.

Preference changes use revision checks and require an explicit reload after uncertain completion. Both account exports retain the preference and budget; account deletion removes the existing user-preference row. The migration adds nullable state plus a revision with a zero default, so existing accounts remain off.

## Acceptance and limits

Domain/API tests exercise UTC/spacing/clock rollback, concurrent budget updates, exact revision conflicts, default off, absence of implicit analysis, stale/dismissed/foreign questions and exports. Component tests cover suppression, stale responses, keyboard navigation and uncertain saves. The real-browser journey covers opt-in, a quiet interval, Zen, dismissal, the server budget, a narrow viewport and accessibility.

The two-per-day/two-hour defaults are conservative prototype choices from the supplied design direction. Actual usefulness and non-intrusion remain user-evaluation questions. This feature does not schedule model work, send browser/OS notifications, recall private material automatically or bypass question review.
