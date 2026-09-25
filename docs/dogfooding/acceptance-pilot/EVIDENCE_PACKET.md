# Machine-readable pilot evidence

[review-packet.template.json](review-packet.template.json) binds P1-P4 from
[cases.json](cases.json) to the existing harness review authoring packet. It is
**NOT RUN**, not a Taskdeck import, execution receipt, UX-scenario schema, or new runner.
Use the existing [manual protocol](README.md) and [session record](session-template.md).

## Diagnostic use

The optional offline reader is implemented in
[agent-harness PR #295](https://github.com/Chris0Jeky/agent-harness/pull/295), building on
[the review/oracle proposal](https://github.com/Chris0Jeky/agent-harness/pull/290).
Use an inspected local checkout of that implementation; do not download and execute a script
from a URL or install a second copy in Taskdeck. From the harness checkout:

```sh
python scripts/review_evidence.py /path/to/Taskdeck/docs/dogfooding/acceptance-pilot/review-packet.template.json
```

The unchanged template reports four requested checks, no candidate observations, and UNKNOWN
identity. `execution_verified` remains false and `merge_verdict` remains null. Exit zero means
that a diagnostic report was produced, not that Taskdeck passed any task or is merge-ready.
The reader does not run a browser, invoke commands from the packet, or retrieve its artifacts.

## Record an actual attempt

Copy the template to the approved local evidence location. Record the selected PR, merge base,
reviewed source head, actual tested revision, fixture digest, and check revision. Replace nulls
only with facts actually established. The pinned task-card source identifies the authoring
contract, not the product revision used in a future run.

Keep every attempt in `observations` with a unique `id`, `check_id` (P1-P4), role (`candidate`,
`successful_control`, or `regression_control`), and status (PASS, FAIL, BLOCKED, NOT RUN, N/A).
Include actual `tested_revision_sha`, `command_or_action`, `environment`, `actual_outcome`, and
`evidence_ref`. These fields describe the observation, not permission to run anything. Use the
[harness field contract](https://github.com/Chris0Jeky/agent-harness/pull/295) for their semantics.

A successful regression control means the intended defect was detected, not necessarily exit
zero. A setup failure is BLOCKED, not a successful negative control. P2 may change proposal
metadata while leaving board state unchanged. P3 must distinguish approval from execution.
Do not overwrite an earlier FAIL with a later PASS. When adding outcome claims, change the
packet header to `RECORDED`; that header still does not authenticate execution.

Run the reader with `--expected-head` using the full source head independently obtained for the
reviewed PR. Preserve actual CI merge SHAs; an OTHER_REVISION result requires manual mapping of
its source/base relationship, not replacing the tested SHA to make the report look clean.

## Boundaries

The reader checks limited structure and evidence-field presence. It cannot verify that an
artifact exists, a command ran, a control was discriminating, or a person found the product
useful. Record comprehension, assistance, friction, and the keep/revise/stop decision in the
session rubric rather than manufacturing an automated human score.

The committed template never contains actual session data. Keep raw results and reports local
by default, preview sanitized publication explicitly, and keep synthetic activity out of the
real dogfood snapshot totals. This adds no CI invocation, product gate, collector, or automatic
export. The original brief remains unresolved as B0 in the harness proposal; these are new
implementation choices, not findings attributed to unseen research.
