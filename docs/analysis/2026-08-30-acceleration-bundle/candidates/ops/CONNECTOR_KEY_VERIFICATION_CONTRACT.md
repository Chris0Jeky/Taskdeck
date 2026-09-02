# Connector-key verification command contract

Recommended command:

```text
taskdeck ops verify-connector-key [--database <path>] [--json]
```

## Contract

- Resolve the same key source and envelope implementation as the running API/CLI. Never invent a parallel crypto path.
- Read encrypted rows without calling external connectors.
- Decrypt each row in memory, validate only the envelope/plaintext shape, then zero caller-owned byte buffers.
- Emit content-free counts and a stable result code. Never print connector usernames, provider identifiers that reveal a user relationship, ciphertext, plaintext, or stack traces by default.
- `NoCredentials` is a distinct, non-proof result. It may exit 0 for first-install usability, but restore/cutover automation must treat it as “not exercised.”
- Wrong key, missing key, malformed envelope, and storage failure are distinct machine-readable outcomes.
- Run this immediately after restore and before serving traffic.

## Suggested JSON result

```json
{
  "schemaVersion": 1,
  "result": "success",
  "checkedCredentialCount": 3,
  "contentExposed": false
}
```

See `ConnectorKeyVerifier.cs` and `OpsCommandExitCodes.cs` for compile-shaped candidates.
