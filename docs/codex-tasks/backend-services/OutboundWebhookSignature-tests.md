# Task: Expand OutboundWebhookSignature tests (1 -> full coverage)

- **GitHub Issue**: [#427](https://github.com/Chris0Jeky/Taskdeck/issues/427) (TST-CODEX-13)
- **Branch**: `test/webhook-signature-expanded-tests`
- **Priority**: Tier 5 (medium)

## Source File

Find `OutboundWebhookSignature.cs` in `backend/src/Taskdeck.Infrastructure/` (search with grep/find).

## Existing Tests

There is 1 existing test. Find it (search for `OutboundWebhookSignature` in `backend/tests/`). Do NOT delete or modify it — add new tests alongside.

## Test Cases to Add

1. Valid signature generation produces expected HMAC-SHA256 output for known input/key pair
2. Signature verification succeeds with correct key and payload
3. Signature verification fails with wrong key
4. Signature verification fails with tampered payload
5. Empty payload produces a valid (non-null, non-empty) signature
6. Null or empty key edge case handling (should throw or return specific error)
7. Large payload signature generation completes without error

## Implementation Notes

- Use the same test project and namespace as the existing test
- Use xUnit attributes
- For deterministic assertions, compute expected HMAC-SHA256 manually or use a known test vector

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~OutboundWebhookSignature"
```

## Acceptance Criteria

- All new + existing tests pass
- 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
