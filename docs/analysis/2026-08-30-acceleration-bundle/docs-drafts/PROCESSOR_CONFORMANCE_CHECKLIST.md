# Processor conformance checklist (draft)

- [ ] Manifest validates against canonical schema and exact enum tokens.
- [ ] Session challenge/proof accepted once and replay rejected.
- [ ] Declared capability/MIME/options enforced.
- [ ] Input arrives only through approved spool/content handle.
- [ ] Fixed fixture result hash deterministic.
- [ ] Cancellation acknowledged inside grace or process tree killed.
- [ ] Deadline, memory and output limits map to stable outcomes.
- [ ] Stderr bounded/content-free; stdout protocol-only.
- [ ] Malformed/null/oversized output rejected.
- [ ] Local-only network denied.
- [ ] Crash replays idempotently after lease expiry.
- [ ] Processor/model/configuration/usage provenance round-trips.
- [ ] Spool/process resources cleaned after every terminal state.
- [ ] Default install works with processor disabled/unavailable.
