# Admissibility, revocation and bounded exposure

Date: 2026-09-10. Related: #2327, #2336 and #2339. Parent: [engineering contract](README.md).

## Auxiliary decision, not a second required gate

`core/admission.mjs` recomputes the continuation plan from protected inputs rather than trusting an uploaded selected list or digest. The entire claimed plan must match. Every task is accounted for once: fresh verified execution, reuse accepted by the current fingerprint/signature checks, or explicitly qualified unaffected selection. Missing, duplicate, unknown, failed, skipped, cancelled, wrong-candidate, wrong-policy, empty-test and retry-erased fresh outcomes reject admission.

Output is `authority:none`, not a ci-run.v1 replacement, and is never posted as a required GitHub check. Taskdeck's canonical gate remains authoritative; shadow mode is unchanged.

`verifyFresh` is a protected-controller callback, not a JSON field. Its default refuses proof. Expected command/environment/input identities are recomputed; producer contracts bind reviewed workflow revision/path/ID and run/job IDs must be present. The callback must authenticate actual execution independently. The fixture's callback returning true simulates that verifier; it is not production provenance.

Omission also requires explicit selection qualification and reviewed contracts. The package cannot grant those approvals itself. Taskdeck contracts remain unreviewed and production reuse disabled. Full qualification bypasses reuse; reused proof retains its original completion/expiry.

**Known pre-activation gap:** the reuse branch does not revalidate current `producerContracts`.
A synthetic reproduction accepts signed non-test evidence after changing the producer workflow
revision and setting `requiresTests: true`, because those contract changes are not independently
bound by this branch. Before authoritative integration, bind the complete producer contract into
versioned evidence identity or revalidate it at admission, including changed workflow paths/revisions
and added test requirements. Keep an unchanged-contract positive control. This is tracked on
[#2336](https://github.com/Chris0Jeky/Taskdeck/issues/2336#issuecomment-5615579153);
`admissible: true` is not a production qualification claim, even with library mode `enforce`.

## Durable reference ledger

`core/ledger.mjs` supplies bounded single-host JSONL append storage, canonical hash chaining, exclusive writer lock, expected-anchor comparison, file fsync, revocation/circuit reduction and age-plus-merge-exposure decisions. Events record revocation, trip, successful complete baseline, explicit recovery or landed exposure. Invalid recovery is rejected before append; historical revocations are not silently cleared.

**Hash chaining is not authentication.** An independently protected latest anchor must be supplied and each returned digest published through a protected compare-and-swap store. Trusting a PR artifact's own final hash protects nothing. The directory, parent filesystem, tooling and anchor provider must be trusted and inaccessible to candidate writes. No production store/key/secret/admin setting is provisioned here.

Append validates the whole current chain against the supplied anchor, validates the new semantic transition, appends/fsyncs and returns the new digest. Concurrent stale-anchor writers fail. Partial writes, corruption, rollback/stale anchors, contention and the 8 MiB capacity ceiling fail closed. Archival/checkpointing requires explicit review; history is never silently dropped.

This is not a distributed database. Shared network filesystems, multi-host locking, power-loss guarantees beyond file fsync and transactional external-anchor publication are not claimed. A crash between append and anchor publication leaves an unanchored tail: disable optimisation and reconcile it against protected execution records before advancing the anchor. Do not auto-truncate tails or break stale locks merely because they are old.

## Circuit and exposure rules

The protected controller should append revocation/trip events after an omitted oracle failure. A trip remains until an explicitly referenced NEWER complete successful baseline covers the affected task. An older baseline or selective green retry cannot recover it. Duplicate baseline identities fail.

`qualificationRequired` checks authenticated state, current policy, full current task-universe coverage, maximum age, maximum landed merges since full baseline and open circuits. Missing/unverified/partial/expired/future state or exhausted limits requires full qualification. Release/R4/other mandatory policy passes mandatory:true; this utility cannot downgrade it.

Permission to use ordinary policy is not successful CI. Landed exposure must come from authenticated events. No event broker is installed; the metadata observer cannot issue full-baseline events. A protected controller must verify actual checkout, complete suite execution and bypassed reuse first.

## Activation and remaining integration

Validate immutable inputs and event/merge bindings, establish independent execution provenance, provide protected anchor/key/revocation storage, qualify selection with frozen-plan/full-oracle recall, rehearse corruption/cancellation/audit misses, then request maintainer review for one family. Merely changing mode to enforce is not activation approval.

Administrative and evidence gates remain open. This PR does not provide a production GitHub fresh-execution verifier or change canonical gate handling. Those integrations remain explicitly outstanding, not represented by a permissive fake verifier.

## Validation and rollback

Combined continuation/placement suite: **352 passed, zero failed/skipped/cancelled**, local Node 22.16.0/Linux. New regressions cover recomputation, stale/empty/skipped/cancelled/retried outcomes, missing verifier and exception redaction, unqualified omission, contention, rollback/corrupt/partial chains, invalid recovery, duplicate baselines and age/exposure circuits.

This is reference-mechanism validation, not production provenance or distributed durability. Hosted configured-Node checks and independent/maintainer review remain required. Revert auxiliary modules without altering canonical qualification; preserve any real ledger and historical revocations separately from code rollback.
