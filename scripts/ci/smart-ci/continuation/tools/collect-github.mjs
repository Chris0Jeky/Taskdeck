#!/usr/bin/env node
import { writeFileSync, appendFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { options } from '../cli.mjs';
import { invariant } from '../core/primitives.mjs';
import { createGitHubReader, collectRun, renderObservation } from '../providers/github.mjs';

export async function collectCli(argv, { token = process.env.GH_TOKEN, fetchImpl = globalThis.fetch } = {}) {
  const o = options(argv, ['--repository', '--repository-id', '--run', '--workflow-id', '--workflow-path', '--head', '--out', '--summary']);
  for (const name of ['--repository', '--repository-id', '--run', '--workflow-id', '--workflow-path', '--out']) invariant(o[name], `missing ${name}`);
  let report;
  try {
    const reader = createGitHubReader({ token, fetchImpl });
    report = await collectRun({ reader, repository: o['--repository'], repositoryId: o['--repository-id'], runId: o['--run'],
      workflowId: o['--workflow-id'], workflowPath: o['--workflow-path'], expectedHead: o['--head'] ?? null });
  } catch (error) {
    // No raw API response body, logs, token, candidate artifact or source text is persisted.
    writeFileSync(resolve(o['--out']), JSON.stringify({ format: 'ci.github-observation.v1', authority: 'none', complete: false, reason: error.message }, null, 2) + '\n', { flag: 'wx' });
    throw error;
  }
  writeFileSync(resolve(o['--out']), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  if (o['--summary']) appendFileSync(resolve(o['--summary']), renderObservation(report));
  return report;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  collectCli(process.argv.slice(2)).then(report => console.log(`Observed run ${report.runId}; ${report.attempts.length} attempts; no qualification authority.`))
    .catch(error => { console.error(error.message); process.exitCode = 1; });
}
