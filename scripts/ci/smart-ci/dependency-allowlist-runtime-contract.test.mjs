import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { buildSummary } from '../summarize-dependency-security-signals.mjs';

test('the runtime allowlist validator rejects unsupported advisory identifiers', async () => {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-allowlist-runtime-'));

  try {
    const backendReport = join(tempDir, 'backend.json');
    const frontendReport = join(tempDir, 'frontend.json');
    const backendExitCode = join(tempDir, 'backend.exitcode');
    const frontendExitCode = join(tempDir, 'frontend.exitcode');
    const allowlist = join(tempDir, 'allowlist.json');

    await Promise.all([
      writeFile(backendReport, JSON.stringify({ projects: [] }), 'utf8'),
      writeFile(
        frontendReport,
        JSON.stringify({
          vulnerabilities: {},
          metadata: {
            vulnerabilities: {
              info: 0,
              low: 0,
              moderate: 0,
              high: 0,
              critical: 0,
              total: 0,
            },
          },
        }),
        'utf8',
      ),
      writeFile(backendExitCode, '0', 'utf8'),
      writeFile(frontendExitCode, '0', 'utf8'),
      writeFile(
        allowlist,
        JSON.stringify({
          schemaVersion: 1,
          entries: [
            {
              advisoryId: 'PACKAGE-WIDE-BYPASS',
              reason: 'This broad identifier must never become an exception key.',
              owner: '@Chris0Jeky',
              expiresOn: '2026-09-30',
            },
          ],
        }),
        'utf8',
      ),
    ]);

    const { summary } = await buildSummary({
      backendReport,
      backendExitCodeFile: backendExitCode,
      frontendReport,
      frontendExitCodeFile: frontendExitCode,
      allowlist,
      policyDoc: null,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext: 'smart-ci-self-test',
      today: '2026-09-17',
    });

    assert.equal(summary.allowlist.valid, false);
    assert.equal(summary.allowlist.activeEntryCount, 0);
    assert.equal(summary.totals.allowlistFailures, 1);
    assert.equal(summary.totals.hasEnforcementFailures, true);
    assert.match(summary.allowlist.errors.join('\n'), /supported advisory identifier/i);
  } finally {
    await rm(tempDir, { recursive: true, force: true });
  }
});
