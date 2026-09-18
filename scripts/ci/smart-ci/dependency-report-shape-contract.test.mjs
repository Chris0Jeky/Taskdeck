import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import { buildSummary } from '../summarize-dependency-security-signals.mjs';

function validBackendReport() {
  return { projects: [] };
}

function validFrontendReport(overrides = {}) {
  return {
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
    ...overrides,
  };
}

async function summarize({ backendReport, frontendReport, backendExitCode = 0, frontendExitCode = 0 }) {
  const tempDir = await mkdtemp(join(tmpdir(), 'taskdeck-dependency-shape-'));

  try {
    const paths = {
      backendReport: join(tempDir, 'backend.json'),
      frontendReport: join(tempDir, 'frontend.json'),
      backendExitCodeFile: join(tempDir, 'backend.exitcode'),
      frontendExitCodeFile: join(tempDir, 'frontend.exitcode'),
      allowlist: join(tempDir, 'allowlist.json'),
    };

    await Promise.all([
      writeFile(paths.backendReport, JSON.stringify(backendReport), 'utf8'),
      writeFile(paths.frontendReport, JSON.stringify(frontendReport), 'utf8'),
      writeFile(paths.backendExitCodeFile, String(backendExitCode), 'utf8'),
      writeFile(paths.frontendExitCodeFile, String(frontendExitCode), 'utf8'),
      writeFile(paths.allowlist, JSON.stringify({ schemaVersion: 1, entries: [] }), 'utf8'),
    ]);

    const { summary } = await buildSummary({
      ...paths,
      policyDoc: null,
      summaryTitle: 'Dependency Security Signal Summary',
      workflowContext: 'smart-ci-self-test',
      today: '2026-09-17',
    });
    return summary;
  } finally {
    await rm(tempDir, { recursive: true, force: true });
  }
}

test('parseable backend JSON without a projects array fails closed', async () => {
  const summary = await summarize({
    backendReport: {},
    frontendReport: validFrontendReport(),
  });

  assert.equal(summary.backend.parseFailed, true);
  assert.equal(summary.backend.scanFailed, true);
  assert.equal(summary.totals.parseFailures, 1);
  assert.equal(summary.totals.hasEnforcementFailures, true);
});

test('parseable frontend JSON without npm audit shape fails closed', async () => {
  const summary = await summarize({
    backendReport: validBackendReport(),
    frontendReport: {},
  });

  assert.equal(summary.frontend.parseFailed, true);
  assert.equal(summary.frontend.scanFailed, true);
  assert.equal(summary.totals.parseFailures, 1);
  assert.equal(summary.totals.hasEnforcementFailures, true);
});

test('a structured npm audit error fails even when the process exit code is zero', async () => {
  const summary = await summarize({
    backendReport: validBackendReport(),
    frontendReport: validFrontendReport({
      error: {
        code: 'EAUDITNETWORK',
        summary: 'mock scanner failure',
      },
    }),
  });

  assert.equal(summary.frontend.parseFailed, false);
  assert.equal(summary.frontend.scanFailed, true);
  assert.equal(summary.totals.scanFailures, 1);
  assert.equal(summary.totals.hasEnforcementFailures, true);
});
