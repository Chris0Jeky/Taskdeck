# Testing material — test ideas and vectors, not pass evidence

Verbatim from `04_TESTING`: `MASTER_TEST_MATRIX.md`, `ADVERSARIAL_CASES.md`, `EXPECTED_ERROR_CODES.md`,
`FIXTURE_CATALOG.md`, `MIGRATION_PROOF_CHECKLIST.md`, `COMPLETION_RECEIPT_TEMPLATE.md` and `test-vectors/`.

Use the matrices and adversarial cases to strengthen repository tests for the owning issue; the vectors are
inputs for those tests. Error codes in `EXPECTED_ERROR_CODES.md` are proposals — the stable-code rule
(400/401/403/404/409) and the existing `ProblemDetails` contracts on `main` win. The bundle's historical
"12/12" result is not evidence for this machine (see `../HEAD_START.md` for the reproduced counts).
