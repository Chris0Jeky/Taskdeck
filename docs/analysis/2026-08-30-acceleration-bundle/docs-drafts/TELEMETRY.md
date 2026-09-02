# Telemetry and feedback (draft)

## Default

Taskdeck self-hosted telemetry is **off by default**. Disabling or failing telemetry never changes capture, processing, review, proposal or apply behavior.

## Feedback without telemetry

The in-app feedback action opens a prefilled GitHub Discussion/issue URL containing only:

- Taskdeck version;
- OS family selected locally;
- feature area selected by the user.

No feedback content is transmitted by Taskdeck itself. The user reviews and submits it in their browser.

## Optional anonymous telemetry

When explicitly enabled, Taskdeck sends only fields listed in the versioned field dictionary. Recommended fields:

- schema/event version;
- random installation identifier hashed with a fixed Taskdeck namespace;
- app version, OS family and install kind;
- aggregate local activation booleans (first capture/proposal/apply);
- feature area and content-free outcome class.

Never send:

- card/capture/transcript/prompt/output text;
- names, email, IP, hostname, username or device identifiers;
- file paths, URLs, clipboard data or connector configuration;
- API keys, tokens, secrets or logs;
- arbitrary exception messages/stack values without a redaction contract.

## Controls

- Settings UI and CLI/status command show current state, endpoint class, field schema and installation ID reset.
- `TASKDECK_TELEMETRY=off` is an emergency override.
- A kill switch disables sends without a release.
- Network calls use a short timeout, bounded queue and no blocking retry.
- Endpoint is registered through Taskdeck egress controls.

## Diagnostics

“Copy diagnostic bundle” is user-triggered and local. It includes versions, OS/runtime family, health state and a bounded recent log excerpt after redaction. The user sees the bundle before pasting it into a report.

## Verification

Every release candidate receives a network-capture check. The receipt records destinations and request classes, not user content. Unexpected egress blocks the release until explained or removed.

## Decisions still required

- first-party endpoint and region;
- raw retention and deletion;
- public aggregate publication;
- hosted-service operational logging versus optional product analytics;
- privacy notice/terms wording.
