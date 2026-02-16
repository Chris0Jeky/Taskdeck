---
name: Security / hardening
about: Track authn/authz and security retrofit work
title: "[Security] "
labels: ["security", "hardening"]
assignees: []
---

## Surface
- Controller / endpoint family:
- Data sensitivity:

## Threat / Misuse Case

## Required Matrix
- [ ] Unauthenticated request returns `401`
- [ ] Authenticated request without access returns `403`
- [ ] Cross-user isolation is enforced
- [ ] Happy path remains valid
- [ ] Error contract is `{ errorCode, message }` where applicable

## Implementation Notes
- Claims-derived actor identity used:
- Query/body actor IDs removed:
- Access checks used:

## Verification
- Integration tests added/updated:
- Manual checks performed:
