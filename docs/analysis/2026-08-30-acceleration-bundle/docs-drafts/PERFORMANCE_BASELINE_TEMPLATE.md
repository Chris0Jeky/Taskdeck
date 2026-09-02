# Performance baseline v0.3 (template)

## Build and machine

- Tag/SHA:
- Dirty tree: yes/no
- Date/time:
- OS/kernel:
- CPU/power plan:
- RAM:
- Disk/filesystem:
- .NET/Node/browser versions:
- Antivirus/background services:
- Dataset generator/version/hash:

## Protocol

- cold/warm definition:
- warm-up runs:
- measured runs:
- concurrency:
- failure handling:
- raw result path/hash:

## Results

| Scenario | Dataset/concurrency | P50 | P95 | Max | Throughput | Failures | Notes |
|---|---|---:|---:|---:|---:|---:|---|
| capture→proposal→apply | | | | | | | |
| board load | 10k cards | | | | | | |
| board load | 100k cards | | | | | | |
| search | | | | | | | |
| review queue | | | | | | | |
| SignalR fan-out | 2/10 clients | | | | | | |
| MCP list/read | HTTP/stdio | | | | | | |
| 500-card render | | | | | | | |
| Lighthouse/bundles | packaged build | | | | | | |

## Ranked candidates

| Rank | Bottleneck evidence | Expected gain | Confidence | Size | Regression risk | Proof target |
|---:|---|---:|---:|---:|---:|---|

Only the top three become implementation issues in this pass.
