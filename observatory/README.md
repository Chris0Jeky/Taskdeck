# Observatory boundary for Taskdeck

Shared portfolio kit: [Pulseboard #15](https://github.com/Chris0Jeky/Pulseboard/pull/15).

Taskdeck already has a consent store, telemetry API, analytics loading boundary and backend operational instrumentation. This integration deliberately does not add the public browser SDK, change CSP, enable a server flag, select a collector origin or override the v0.3 zero-egress policy.

The concrete fix is retry ownership: withdrawing consent invalidates an in-flight batch. A later network failure cannot requeue old events, even after a fresh opt-in. Withdrawal clears memory, rotates the session and stops the timer before attempting storage. Blocked storage must not prevent this cleanup. Already transmitted data cannot be recalled, and storage failures may prevent a choice surviving reload.

Five Vitest regression tests cover withdrawal, withdrawal/re-consent, uninterrupted-consent retries, blocked writes and blocked reads. Run the existing frontend test command targeting `telemetryStore.consent.spec.ts`, followed by the repository's usual checks. These host tests have not been executed in this environment.

For a separately approved v0.4 integration, map Taskdeck's existing reviewed taxonomy to a closed collector contract, keep content fields out, reuse its consent UI, and forward only through an explicitly enabled adapter. Do not send board/card text, prompts, automation payloads, local paths, private memory or imported files. Keep operational metrics separate from opt-in product analytics. The shared registry intentionally rejects public Taskdeck collection today.
