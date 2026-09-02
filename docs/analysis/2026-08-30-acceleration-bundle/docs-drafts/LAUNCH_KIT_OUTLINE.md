# Launch kit outline (draft)

## Claim ledger

| Claim | Shipping evidence | Owner | Last verified | Allowed wording |
|---|---|---|---|---|
| Review-first agent writes | hostile-write/proposal tests | engineering | date/SHA | exact bounded claim |
| Local/self-host data ownership | storage/egress docs and network capture | engineering | date/SHA | no stronger than evidence |
| Hosted availability | status/operations receipt | operator | date | beta expectation, not SLA |
| Backup/restore | timed production-image drill | operator | date | measured RPO/RTO |
| Telemetry posture | TELEMETRY.md + release network capture | maintainer | date/SHA | off by default / exact fields |

## Synthetic demo workspace

- inbox captures from typed note, transcript and document;
- proposal with evidence links;
- review/apply history;
- hierarchy, relation, assignment and custom-field examples only if shipped;
- no production/customer data.

## Media

- architecture diagram;
- capture → proposal → review → apply sequence;
- hosted-beta gate diagram;
- 90-second narrated script;
- short GIF/video clips with captions;
- screenshots at consistent viewport and synthetic state.

## Probe-answer bank

- What phones home?
- Where is data stored?
- What can an agent change directly?
- What happens when the LLM/worker fails?
- How are backups restored?
- Who pays for hosted LLM use?
- What are the current SQLite/single-node limits?
- Is the license intended to remain stable?
- What is not shipped yet?

## Launch operations

- choose day with 48-hour maintainer availability;
- known-issues pinned post;
- bug/question/idea triage routing;
- same-day issue creation for reproducible defects;
- status page and close-registration plan;
- day-1/day-7/day-30 metrics template.

Agents prepare drafts and evidence. The maintainer posts under their own accounts.
