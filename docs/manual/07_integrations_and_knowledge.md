# Manual Chapter 07: Integrations And Knowledge

Taskdeck ships connector management at `/workspace/integrations`. Open it through `Search` / `Ctrl+K`; it is part of the visible operator group in Workbench mode and Guided mode's explicit `Advanced` disclosure.

From this page you can:

- register a named connector with its type, direction, and optional JSON configuration
- inspect connector details and recent state
- enable or disable a connector
- delete a connector you no longer need

The shipped connector types include browser clipper, Markdown import, web clip, GitHub issue intake, inbound webhook, and custom connectors. Inbound connector content enters Taskdeck through the capture pipeline; it does not bypass proposal review and explicit execution.

Taskdeck does not yet ship a standalone `Knowledge` route. Existing search, artefact, and connector foundations should not be described as a user-facing Knowledge workspace until that route and its product contract land.

## See Also

- [04_inbox_and_review.md](04_inbox_and_review.md)
- [05_advanced_automation.md](05_advanced_automation.md)
- [06_agents.md](06_agents.md)
