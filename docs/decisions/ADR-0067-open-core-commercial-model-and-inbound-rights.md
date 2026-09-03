# ADR-0067: Commercial Model — Open-Core with Managed Hosting and Services; Inbound Contributions Paused Pending a Relicensing-Capable Instrument

- **Status**: Accepted (maintainer decision packet, 2026-09-03 — rulings COMM_MODEL and
  COMM_INBOUND, recorded on `#2012`, which closes on its own stop criterion). No licence
  transition is chosen, so ADR-0050 stays in force unchanged; this record adds the business model
  and the inbound-rights precondition that `#2012` left open.
- **Date**: 2026-09-03
- **Deciders**: Maintainer (Chris). Agent pass recorded the ruling; nothing here was inferred.
- **Related**: ADR-0050 (GPL-3.0-only core), ADR-0061 (trusted shared instance boundary),
  ADR-0066 (private repository for v0.3.0), `docs/REVIVAL_PLAN.md` §2 (commitments and
  monetization sequencing), `docs/strategy/PRODUCT_DIRECTION.md` §7, `#2012`, `#1482`
  (name/legal residuals), `#2439` (public release/source mirror)

## Context

`#2012` recorded the tension between an earlier intention (GPL beta, then a proprietary full
release) and the binding commitments already made: ADR-0050's GPL-3.0-only core, the
`LICENSING.md` free boundary, and DCO inbound-equals-outbound with no relicensing assignment. The
2026-08-24 audit established the facts that make a choice possible now rather than later: a
single-human-author history, no external human contributor ever, no vendored code, no copyleft
direct dependency, and consistent licence metadata. External code contributions were paused the
same day so that no inbound rights question could be created before the model was chosen.

## Decision

1. **Open-core with managed hosting and services** is the business model. The capture → proposal →
   review → apply core, data export/portability, BYO-key and local-LLM use, and single-user
   self-hosting stay in the GPL-3.0-only core exactly as `docs/REVIVAL_PLAN.md` §2 commitment 3
   states. Revenue comes from a managed hosted instance and services around it, and from
   explicitly separately licensed modules (the reserved `ee/` pattern) that are *additive* — nothing
   already shipped in the core is later removed from it. The hosted control plane stays private
   from day one (commitment 4). No proprietary transition of the core is chosen, so no ADR
   supersedes ADR-0050 and `LICENSE`/`LICENSING.md` keep their terms.
2. **External contributions stay paused.** They reopen only after a relicensing-capable inbound
   instrument — a contributor licence agreement or an equivalent contribution grant — is drafted,
   reviewed, and stated in `CONTRIBUTING.md`. DCO alone does not preserve the flexibility an
   open-core model needs for the separately licensed modules, so the DCO-only posture is not
   reinstated as the reopening condition. Drafting the instrument is deferred until contributions are
   about to reopen; it is not started by this record.
3. **Sequencing stays as recorded.** The monetization order in `docs/REVIVAL_PLAN.md` §2 (wide-open
   beta measuring activation and retention → flat-priced hosted instance → team tier → enterprise)
   is now anchored by this ADR instead of a future one. The name/legal residuals on `#1482` remain
   the commercial gate: attorney opinion and registrations happen before paid or commercial
   distribution, not before the free beta.

## Alternatives Considered

- **Proprietary core after the beta.** Rejected: contradicts ADR-0050 and the public free-boundary
  commitments, and distributed GPL copies keep their rights anyway; the audit shows the option
  remains *legally* open for the maintainer's own code, but the model chosen does not need it.
- **Dual licence (GPL + commercial) of the whole core.** Not chosen now; it becomes possible only
  with a relicensing-capable inbound instrument, which decision 2 requires before any external
  contribution is accepted, so the option is preserved rather than exercised.
- **Source-available core.** Rejected: it weakens the self-hosting and portability promises that
  differentiate the product without adding revenue the managed instance does not already capture.
- **Managed-service only, no separately licensed modules.** Not chosen: the `ee/` pattern stays
  reserved so that team/enterprise features can be licensed separately if the sequencing reaches
  them; nothing is built under it yet.
- **Reopen contributions under DCO now.** Rejected: it would create exactly the inbound-rights
  constraint `#2012` warned about before the instrument exists.

## Consequences

- `CONTRIBUTING.md`, `LICENSING.md` and the README notice state the decided model and the
  reopening condition instead of "under evaluation".
- `docs/strategy/PRODUCT_DIRECTION.md` §7 item 1 and `docs/REVIVAL_PLAN.md` §2 commitment 2 are
  amended to point here; `OUTSTANDING_TASKS.md` closes the `#2012` row.
- The private development repository with a public release/source mirror (ADR-0066 SC-8 ruling,
  `#2439`) is compatible with this model: the GPL-covered source remains published for every release.
- Residual risk: the choice is recorded without legal advice; if counsel later requires a different
  instrument or boundary, this ADR is amended, not silently bypassed.

## References

- `#2012` — decision surface and the 2026-08-24 copyright/contribution audit
- `docs/decisions/ADR-0050-gplv3-copyleft-core.md`
- `docs/REVIVAL_PLAN.md` §2 — commitments and monetization sequencing
- `docs/strategy/PRODUCT_DIRECTION.md` §7 — open decision surfaces
- Maintainer decision packet, 2026-09-03 (rulings COMM_MODEL, COMM_INBOUND, LEGAL, SC8)
