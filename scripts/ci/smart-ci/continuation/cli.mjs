#!/usr/bin/env node
import { readFileSync, writeFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { invariant } from './core/primitives.mjs';
import { inspectRepository, starterManifest, validateManifest } from './adapters/repository.mjs';

export function options(argv, allowed) {
  const out = {};
  invariant(argv.length % 2 === 0, 'options require explicit values');
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i];
    invariant(allowed.includes(key) && !Object.hasOwn(out, key) && argv[i + 1] && !argv[i + 1].startsWith('--'), `unknown, duplicate or missing option: ${key}`);
    out[key] = argv[i + 1];
  }
  return out;
}
function required(o, keys) { for (const k of keys) invariant(o[k], `missing ${k}`); }
export function runCli(argv) {
  const [command, ...args] = argv;
  if (command === '--help') return { usage: ['init --kind node|dotnet|python --repository-id ID --root PATH --out NEW_FILE',
    'validate --manifest FILE', 'plan --repo REPO --base FULL_SHA --candidate FULL_SHA --repository-id ID --config .ci/continuation.json --out NEW_FILE'],
    authority: 'Observation only. No task commands, workflow edits, network or status writes.' };
  let result, output;
  if (command === 'init') {
    const o = options(args, ['--kind', '--repository-id', '--root', '--out']); required(o, ['--kind', '--repository-id', '--out']);
    result = starterManifest({ kind: o['--kind'], repositoryId: o['--repository-id'], root: o['--root'] ?? '.' }); output = o['--out'];
  } else if (command === 'validate') {
    const o = options(args, ['--manifest']); required(o, ['--manifest']);
    invariant(statSync(o['--manifest']).size <= 1024 * 1024, 'manifest exceeds budget');
    const text = readFileSync(o['--manifest'], 'utf8'); invariant(Buffer.byteLength(text) <= 1024 * 1024, 'manifest exceeds budget');
    const manifest = validateManifest(JSON.parse(text)); result = { valid: true, repositoryId: manifest.repositoryId, tasks: Object.keys(manifest.contracts.tasks), authority: 'none' };
  } else if (command === 'plan') {
    const o = options(args, ['--repo', '--base', '--candidate', '--repository-id', '--config', '--out']);
    required(o, ['--repo', '--base', '--candidate', '--repository-id', '--out']);
    result = inspectRepository({ repo: resolve(o['--repo']), base: o['--base'], candidate: o['--candidate'], repositoryId: o['--repository-id'], configPath: o['--config'] ?? '.ci/continuation.json' });
    output = o['--out'];
  } else throw new Error('unknown command; use --help');
  if (output) writeFileSync(resolve(output), JSON.stringify(result, null, 2) + '\n', { flag: 'wx' });
  return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { console.log(JSON.stringify(runCli(process.argv.slice(2)), null, 2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
