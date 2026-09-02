# Validation report

Validated against the generated bundle on 2026-08-30.

## Passed

- Bundle integrity verifier: required files, JSON parsing, issue coverage, task dependencies, manifest hashes and checksums.
- Task queue validator: 116 unique contracts and valid internal task dependencies/status vocabulary.
- Python utility suite: **12/12 tests passed**.
- API shard fixture: 10 test classes form an exact partition across 7 shards.
- Quarantine fixture: schema/governance validation passes for the dated sample.
- Telemetry fixture: explicit allowlist/content denylist validation passes.
- Weekly CI report fixture: report generated from the sample receipt.
- JSON Schema: all five schemas are valid Draft 2020-12 schemas; CI receipt and quarantine samples validate.
- Python syntax: every generated `.py` file compiles.
- Graphviz: 10 DOT sources rendered to 10 SVG and 10 PNG files.

## Deliberately not claimed

The C# files are **compile-shaped implementation candidates**, not repository patches. They were source-reviewed but could not be compiled in this artifact container because a .NET SDK is not installed. The ingestion agent must adapt namespaces/contracts to live Taskdeck, run `dotnet build`, and execute the supplied candidate tests before adoption.

No live Taskdeck branches, PRs, issue comments, migrations, hosted infrastructure or external launch actions were created by this bundle.

## Re-run

```bash
python3 07_AGENT_HANDOFF/scripts/verify_bundle.py .
python3 07_AGENT_HANDOFF/scripts/validate_task_queue.py 01_MILESTONE_5/task-queue.json
python3 -m unittest discover -s 04_TESTING/python-tests -p 'test_*.py' -v
```
