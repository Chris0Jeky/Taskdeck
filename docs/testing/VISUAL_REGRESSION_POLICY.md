# Visual Regression Policy

Last Updated: 2026-04-09

## Purpose

Visual regression tests capture baseline screenshots of key UI surfaces and compare them against future renders. The goal is to catch unintended layout, color, or structural changes before they reach users, while minimizing false positives from non-deterministic rendering differences.

## Covered Surfaces

The visual regression suite covers these critical UI areas:

| Surface | Test file | Baseline screenshots |
|---------|-----------|---------------------|
| Board (empty) | `board-view.visual.spec.ts` | `board-empty.png` |
| Board (populated) | `board-view.visual.spec.ts` | `board-populated.png` |
| Command palette (open) | `command-palette.visual.spec.ts` | `command-palette-open.png` |
| Command palette (search) | `command-palette.visual.spec.ts` | `command-palette-search.png` |
| Archive (empty) | `archive-view.visual.spec.ts` | `archive-empty.png` |
| Inbox/capture (empty) | `inbox-capture.visual.spec.ts` | `inbox-empty.png` |
| Home view | `home-view.visual.spec.ts` | `home-default.png` |

## Threshold Settings

These settings are configured in `playwright.visual.config.ts`:

| Setting | Value | Rationale |
|---------|-------|-----------|
| `maxDiffPixelRatio` | `0.005` (0.5%) | Allows minor sub-pixel differences while catching real layout shifts |
| `threshold` | `0.3` | Per-pixel color distance tolerance (0-1 scale). Absorbs anti-aliasing differences |
| `animations` | `disabled` | Prevents non-deterministic frame captures |
| Viewport | `1280x720` | Fixed size eliminates responsive layout variance |
| `reducedMotion` | `reduce` | CSS `prefers-reduced-motion` suppresses transitions |
| `colorScheme` | `light` | Forces light mode for consistent color baselines |

## False-Positive Mitigation

### Font Rendering

Font rendering varies significantly across operating systems (macOS, Windows, Linux). The visual tests use:

- **Single canonical platform**: Baselines are generated on `ubuntu-latest` (matching CI). Local development on other OSes should use `npm run test:visual:update` to generate local baselines, but only ubuntu-generated baselines should be committed. This avoids cross-platform baseline conflicts.
- **Elevated color threshold**: The `threshold: 0.3` setting absorbs sub-pixel anti-aliasing differences that may still occur within the same OS (e.g., different GPU drivers on CI runners).
- **maxDiffPixelRatio tolerance**: Up to 0.5% of pixels can differ without failing.

### Animations and Transitions

All animations are disabled through multiple layers:

1. **Playwright `animations: 'disabled'`**: Built-in screenshot option.
2. **`reducedMotion: 'reduce'`**: CSS media query that well-behaved CSS respects.
3. **Injected CSS**: The `hideDynamicContent()` helper forcibly sets `animation-duration: 0s` and `transition-duration: 0s` on all elements.

### Dynamic Content

The `hideDynamicContent()` helper applies the following rules:

- **Timestamp selectors** (forward-looking): `[data-testid="timestamp"]`, `[data-testid="relative-time"]`, `time` tags are hidden via `visibility: hidden`. Note: the current codebase renders timestamps as inline text in plain `<span>`/`<p>` tags without these attributes, so these selectors are not yet effective. When adding visual tests for populated views, add `data-testid="timestamp"` to the relevant Vue components.
- **Blinking cursors**: transparent caret color on all elements
- **Platform-specific scrollbars**: hidden via `::-webkit-scrollbar` and `scrollbar-width: none`

### Network Stability

The `waitForVisualStability()` helper:

1. Waits for `networkidle` state (all API responses received)
2. Waits for all `<img>` elements to load
3. Adds a 300ms paint stabilization pause

## Baseline Management

### Where Baselines Live

Baseline screenshots are stored in:
```
frontend/taskdeck-web/tests/visual/__screenshots__/
```

These files are **committed to the repository**. This is intentional:
- Baselines are reviewable in PRs (GitHub renders image diffs)
- Changes to baselines require explicit approval
- History is preserved in git

### Generating Initial Baselines

When adding a new visual test or running for the first time:

```bash
cd frontend/taskdeck-web
npm run test:visual:update
```

This runs all visual tests and saves the current render as the baseline. Review the generated images before committing.

### Updating Baselines

When a legitimate UI change causes visual test failures:

1. **Verify the change is intentional** by reviewing the diff artifacts from CI
2. **Update baselines locally**:
   ```bash
   cd frontend/taskdeck-web
   npm run test:visual:update
   ```
3. **Review updated baselines** before committing:
   - Check that only the expected views changed
   - Verify no unintended regressions in other screenshots
4. **Commit baseline changes in a dedicated commit** (separate from code changes) so reviewers can clearly identify what changed visually
5. **PR reviewers should inspect baseline image diffs** using GitHub's image diff viewer

### CI Baseline Generation

For CI, baselines must be generated on `ubuntu-latest` to match the CI environment. If baselines were generated on a different OS, CI will fail due to font rendering differences.

The CI workflow automatically detects when no baselines exist and runs with `--update-snapshots` to generate them. The generated baselines are uploaded as the `visual-regression-baselines` artifact. To bootstrap baselines for the first time or after a full reset:

1. Push the branch and trigger the visual regression CI job
2. Download the `visual-regression-baselines` artifact from the CI run
3. Place the files in `frontend/taskdeck-web/tests/visual/__screenshots__/`
4. Commit and push

To regenerate CI-compatible baselines after intentional UI changes:
1. Download the `visual-regression-diffs` artifact from the failing CI run
2. Review the `*-actual.png` images to verify the changes are intentional
3. Download the actual images and place them as the new baselines in `__screenshots__/`
4. Commit and push

Alternatively, if you have access to an identical Ubuntu environment (Docker, WSL2 with matching fonts), generate baselines there.

## CI Integration

Visual regression tests run in the **CI Extended** pipeline:

- **Trigger**: PRs with `testing` or `visual` labels, or manual `workflow_dispatch`
- **Runner**: `ubuntu-latest` (canonical baseline platform)
- **Artifacts on failure**: `visual-regression-diffs` (test-results with actual/diff images) and `visual-regression-report` (Playwright HTML report)
- **Not a merge gate**: Visual tests run in CI Extended, not CI Required. This prevents font rendering differences from blocking PRs while still providing visual change visibility.

### Reviewing CI Failures

When visual tests fail in CI:

1. Download the `visual-regression-diffs` artifact
2. Look for `*-actual.png` and `*-diff.png` files alongside the expected baselines
3. If the diff shows a legitimate regression: fix the code
4. If the diff shows an intentional change: update baselines (see above)
5. If the diff appears to be a false positive: consider adjusting thresholds and document the finding

## Running Locally

```bash
cd frontend/taskdeck-web

# Run visual tests against current baselines
npm run test:visual

# Update baselines to current state
npm run test:visual:update

# Run a single visual test file
npx playwright test --config playwright.visual.config.ts tests/visual/board-view.visual.spec.ts
```

Note: Local baselines may differ from CI baselines due to font rendering. The committed baselines should match the CI platform (Ubuntu).

## Adding New Visual Tests

1. Create a new `*.visual.spec.ts` file in `frontend/taskdeck-web/tests/visual/`
2. Follow the existing pattern: register session, navigate, prepare, screenshot
3. Use `prepareForScreenshot()` before every `toHaveScreenshot()` call
4. Generate baselines: `npm run test:visual:update`
5. Add the new surface to the table at the top of this document
6. Commit baselines in a separate commit for clear PR review
