# Manual Chapter 07: Integrations And Knowledge

Taskdeck ships a connector registry at `/workspace/integrations`. Open it through `Search` / `Ctrl+K`; it is part of the visible operator group in Workbench mode and Guided mode's explicit `Advanced` disclosure. This screen stores and manages connector definitions; it is not yet a runtime for receiving or fetching external content.

From this page you can:

- register a named connector with its type, direction, and optional JSON configuration
- inspect connector details and recent state
- enable or disable a connector
- delete a connector you no longer need

The registry offers browser clipper, Markdown import, web clip, GitHub issue intake, inbound webhook, and custom connector types. Registering or enabling one records its definition and lifecycle state only; no registered connector currently fetches, receives, or routes content into Taskdeck.

Taskdeck does ship standalone Markdown note import and web-clip paste capture routes. Those routes send submitted content through the capture pipeline and remain separate from the connector registry; they do not execute registered connector definitions. When connector runtimes are added, inbound content must use the same review-first capture path rather than mutate a board directly.

Taskdeck does not yet ship a standalone `Knowledge` route. Existing search, artefact, and connector foundations should not be described as a user-facing Knowledge workspace until that route and its product contract land.

## See Also

- [04_inbox_and_review.md](04_inbox_and_review.md)
- [05_advanced_automation.md](05_advanced_automation.md)
- [06_agents.md](06_agents.md)
