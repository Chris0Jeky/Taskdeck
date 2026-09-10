#!/usr/bin/env node
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { closure, invariant } from '../core/primitives.mjs';

// Intentionally a narrow transform for Taskdeck's block-mapping workflow shape,
// NOT a general YAML parser. Unknown shapes fail instead of guessing.
const EXPECTED = ['docs-governance', 'release-workflow-contract', 'backend-architecture', 'backend-unit',
  'api-integration', 'migration-validation', 'frontend-unit', 'paper-color-audit', 'container-images',
  'secret-scan', 'dependency-security', 'sast-scan', 'e2e-smoke'];
const MINIMAL = {
  'backend-unit': ['backend-architecture', 'release-workflow-contract'],
  'frontend-unit': ['release-workflow-contract', 'paper-color-audit'],
  'api-integration': ['backend-architecture', 'release-workflow-contract'],
  'migration-validation': ['backend-architecture'],
  'container-images': ['release-workflow-contract'],
  'e2e-smoke': ['frontend-unit', 'release-workflow-contract', 'paper-color-audit']
};
function splitJobs(text) {
  invariant(!text.includes('\r'), 'normalize CRLF explicitly before staging');
  invariant(!/^[^#\n]*[&*][A-Za-z_][\w-]*/m.test(text), 'YAML anchors/aliases are not supported');
  const offset = text.indexOf('\njobs:\n');
  invariant(offset >= 0 && text.indexOf('\njobs:\n', offset + 1) < 0, 'one block-mapping jobs section required');
  const header = text.slice(0, offset + 7), rest = text.slice(offset + 7);
  invariant(!/^\S/m.test(rest), 'unexpected top-level content after jobs');
  const starts = [...rest.matchAll(/^  ([a-z][a-z0-9-]*):\n/gm)];
  const jobs = new Map();
  for (let i = 0; i < starts.length; i++) {
    const match = starts[i];
    invariant(!jobs.has(match[1]), 'duplicate job id');
    jobs.set(match[1], rest.slice(match.index, starts[i + 1]?.index ?? rest.length));
  }
  invariant(starts[0]?.index === 0, 'unsupported jobs preamble');
  invariant(EXPECTED.every(id => jobs.has(id)) && jobs.size === EXPECTED.length, 'job inventory changed; re-review staging contract');
  return { header, jobs };
}
function parseNeeds(block) {
  const occurrences = [...block.matchAll(/^    needs:.*$/gm)];
  invariant(occurrences.length <= 1, 'duplicate needs mapping');
  if (!occurrences.length) return { needs: [], range: null };
  invariant(occurrences[0][0] === '    needs:', 'only block-list needs is supported');
  const start = occurrences[0].index;
  const match = block.slice(start).match(/^    needs:\n((?:      - [a-z][a-z0-9-]*\n)+)/);
  invariant(match, 'unsupported needs list');
  const end = start + match[0].length;
  invariant(!/^ {5,}\S/.test(block.slice(end)), 'unsupported trailing needs content');
  const needs = [...match[1].matchAll(/- ([a-z][a-z0-9-]*)/g)].map(x => x[1]);
  invariant(new Set(needs).size === needs.length, 'duplicate dependency');
  return { needs, range: [start, end] };
}
function withoutNeeds(block) {
  const { range } = parseNeeds(block);
  return range ? block.slice(0, range[0]) + block.slice(range[1]) : block;
}
export function stageWorkflow(text, mode = 'minimal') {
  invariant(['minimal', 'compute'].includes(mode), 'mode must be minimal or compute');
  const { header, jobs } = splitJobs(text);
  const additions = structuredClone(MINIMAL);
  if (mode === 'compute') additions['api-integration'].push('backend-unit');
  const changes = [];
  const result = new Map(jobs);
  for (const [id, extra] of Object.entries(additions)) {
    const block = jobs.get(id), parsed = parseNeeds(block);
    const needs = [...new Set([...parsed.needs, ...extra])];
    const added = needs.filter(x => !parsed.needs.includes(x));
    if (added.length === 0) continue;
    const replacement = `    needs:\n${needs.map(x => `      - ${x}\n`).join('')}`;
    const position = parsed.range ?? [block.indexOf('\n') + 1, block.indexOf('\n') + 1];
    const updated = block.slice(0, position[0]) + replacement + block.slice(position[1]);
    invariant(withoutNeeds(updated) === withoutNeeds(block), 'non-dependency workflow content changed');
    result.set(id, updated); changes.push({ job: id, added });
  }
  const nodes = Object.fromEntries([...result].map(([id, block]) => [id, { deps: parseNeeds(block).needs }]));
  for (const id of Object.keys(nodes)) closure(id, nodes);
  invariant(!Object.values(nodes).some(x => x.deps.includes('secret-scan')), 'PR-only secret job cannot be an unconditional barrier');
  return { text: header + [...result.values()].join(''), changes, mode };
}

function main() {
  const args = process.argv.slice(2), options = {};
  for (let i = 0; i < args.length; i += 2) {
    invariant(['--repo', '--out', '--mode'].includes(args[i]) && args[i + 1], 'usage: node tools/stage-taskdeck.mjs --repo REPO --out NEW_FILE [--mode minimal|compute]');
    options[args[i].slice(2)] = args[i + 1];
  }
  invariant(options.repo && options.out, '--repo and --out required; original file is never overwritten');
  const source = resolve(options.repo, '.github/workflows/ci-required.yml'), out = resolve(options.out);
  invariant(source !== out && !existsSync(out), 'output must be a new, separate file');
  const result = stageWorkflow(readFileSync(source, 'utf8'), options.mode ?? 'minimal');
  writeFileSync(out, result.text, { flag: 'wx' });
  console.log(JSON.stringify({ output: out, changes: result.changes, mode: result.mode,
    warning: 'Local proposed file only. Lint, run contracts, review diff, then open a PR. Every existing test remains selected.' }, null, 2));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
