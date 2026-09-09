# Night palette candidate checkpoint

This numeric evidence package supports the maintainer choice requested in [issue #2009](https://github.com/Chris0Jeky/Taskdeck/issues/2009). It does not select or adopt a palette. The shipped primary ink value stays exact, all accent and status tokens stay exact, and the 3:1 non-text boundary target is reported as an aspiration rather than a release gate.

The package compares the current night palette with three warm directions:

- **A. Walnut outline** adds the clearest improvement with the smallest surface change.
- **B. Open folio** lightens the page substrate around recessed dark cards, reducing page-scale primary-ink contrast.
- **C. Cedar layers** makes cards rise from a dark substrate and gives the strongest surface hierarchy.

[Measured palette data](./contrast-summary.md) contains the issue comparison table, method, and complete CSV matrix. The [rendered token receipt](./rendered-token-measurements.json) records browser-computed values at the real render roots, selected Paper mode, viewport, PNG dimensions, and the synthetic fixture identifiers. The [local contact sheet](./harness/contact-sheet.html) compares the full-resolution Home, Today, and Review captures at 1440 by 1000.

The capture harness uses the repository Playwright fixture helpers with one synthetic Mock user, board, and pending capture proposal. The user selection is explicit: each page navigation seeds `td.paper.mode.v2` with `paper` or `paper-night`; the harness does not infer a theme from computed colors. The fixed capture title is `Night palette proof`, while generated user, board, capture, and proposal IDs remain in the receipt for traceability. No real data or remote provider was used.

The current night baseline keeps the shipped behavior. It records a real parity finding on Review: the body is selected as `paper-night`, but `PaperReviewView` mounts a static `.paper` root, so its inner surface computes the shipped light tokens. Candidate Review rows are clearly experimental comparisons: the harness applies the full night token set plus the selected `palettes.json` surface overrides to the real `.paper` root with page-local CSS only. This makes the candidate surfaces comparable without claiming a production fix or palette adoption. Home and Today inherit the selected body scope directly.

The harness asserts the Playwright page viewport and PNG IHDR dimensions are both exactly 1440 by 1000, verifies selected candidate values on the document and actual surface roots, and checks that populated app text is present before each capture. Its source is retained under [`harness/`](./harness/); the disposable Mock database and server state are excluded from delivery.
