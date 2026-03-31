# ADR-0014: Platform Expansion Strategy — Four Pillars

- **Status**: Proposed (roadmap)
- **Date**: 2026-03-29
- **Deciders**: Project maintainers

## Context

With the core product loop (capture → review → board) shipped and demo-ready, the next strategic question was: what's the path from local-first developer tool to commercially viable product? Multiple directions competed for attention: packaging, cloud hosting, mobile, community building.

## Decision

Organize platform expansion into four strategic pillars, each with its own tracker:

1. **Market Adoption** (`#544`): Self-serve onboarding, community, content marketing, beta intake workflow
2. **Packaging & Distribution** (`#532`): Self-contained exe → installer → container → package managers
3. **Cloud & Collaboration** (`#537`): Hosted multi-tenant instance, GitHub OAuth, team features
4. **Mobile Platform** (`#540`): PWA first, then native iOS/Android considerations

Version roadmap:
- `v0.1.0` — Self-contained executable
- `v0.2.0` — Hosted cloud instance
- `v0.3.0` — PWA / mobile
- `v0.4.0` — Collaboration features
- `v0.5.0` — Platform maturity
- `v1.0.0` — General Availability

## Alternatives Considered

- **Cloud-first**: Skip packaging, go straight to hosted; rejected because local-first is a differentiator and packaging enables offline use.
- **Mobile-first**: High market potential but premature before desktop experience is polished.
- **Open-source community-first**: Valuable but requires stable packaging and docs first.

## Consequences

- **Positive**: Clear sequencing prevents scope creep; each pillar has an owner tracker; version milestones create natural release boundaries.
- **Negative**: Four-pillar scope is ambitious; risk of spreading effort too thin.
- **Neutral**: Strategy docs live in `docs/strategy/`; master tracker at `#531`.

## References

- `docs/strategy/` — pillar documents
- Master tracker: `#531`
- `docs/STATUS.md` — platform expansion entry
