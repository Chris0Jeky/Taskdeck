#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync, realpathSync, existsSync, rmSync } from 'node:fs';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { snapshot } from '../core/snapshot.mjs';
import { invariant } from '../core/primitives.mjs';
import { options } from '../cli.mjs';

export const KIT_PREFIX = 'scripts/ci/smart-ci/continuation/';
export const PORTABLE_FILES = Object.freeze([
  'core/primitives.mjs', 'core/snapshot.mjs', 'core/contracts.mjs', 'core/evidence.mjs', 'core/planner.mjs',
  'core/execution.mjs', 'core/audit.mjs', 'core/admission.mjs', 'core/ledger.mjs',
  'adapters/repository.mjs', 'providers/github.mjs', 'cli.mjs', 'tools/collect-github.mjs', 'tools/verify-export.mjs',
  'examples/sample-model.mjs', 'examples/demo.mjs', 'tests/fixtures.mjs',
  'tests/core.test.mjs', 'tests/evidence.test.mjs', 'tests/execution.test.mjs', 'tests/planner.test.mjs',
  'tests/repository.test.mjs', 'tests/github.test.mjs', 'tests/admission-ledger.test.mjs'
]);
const inside = (root, path) => { const r = relative(root, path); return r === '' || !isAbsolute(r) && r !== '..' && !r.startsWith(`..${sep}`); };
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const README = `# Portable CI continuation kit

This export contains provider-neutral libraries, a conservative Node/.NET/Python repository adapter, a read-only GitHub observer, synthetic examples and regression tests. It contains no Taskdeck workflow, live token, private key, configured ledger or production reuse activation.

Run from this directory with Node 22+ and Git:

    node --test
    node examples/demo.mjs
    node tools/verify-export.mjs --dir .
    node cli.mjs init --kind node --repository-id YOUR_NUMERIC_REPO_ID --root . --out ../candidate-config.json
    node cli.mjs validate --manifest ../candidate-config.json

Review the starter against actual commands, dependencies, fixtures and runtime inputs before committing it as .ci/continuation.json. The plan command reads that configuration from an immutable base commit, never candidate code:

    node cli.mjs plan --repo /path/to/repo --base FULL_BASE_SHA --candidate FULL_CANDIDATE_SHA --repository-id YOUR_NUMERIC_REPO_ID --out ../advisory.json

All adapters are observation-only. Enforce-mode examples are fictional; importing them grants no permission to omit checks. Do not use REST success metadata as execution provenance. Admission defaults to rejecting fresh proof without a protected verifier. Ledger hash chains require an independently protected anchor; the reference filesystem store is not a distributed database.

Read Taskdeck docs/ci/continuation for complete engineering, adapter, observer, admission, rollback and activation requirements. Supply equivalent protected-controller integration and independent full-audit evidence before activating any optimisation in another repository.

LICENSE is copied byte-for-byte from the source commit. This export adds no licence grant, exception or package publication. The manifest contains source Git identities and file checksums, not a signature or proof of trusted authorship. Verification only checks integrity against that manifest. Keep the original manifest in a trusted location.
`;

/** Immutable-object export. Never scans/copies the worktree or executes repository code. */
export function exportKit({ repo, commit, out }) {
  const source = realpathSync(repo), target = resolve(realpathSync(dirname(resolve(out))), resolve(out).split(sep).at(-1));
  invariant(!inside(source, target) && !inside(target, source) && !existsSync(target), 'export must be a new directory outside the source repository');
  const state = snapshot(source, commit), byPath = new Map(state.entries.map(e => [e.path, e]));
  const files = []; let total = 0;
  for (const name of [...PORTABLE_FILES, 'LICENSE'].sort()) {
    const sourcePath = name === 'LICENSE' ? name : KIT_PREFIX + name, entry = byPath.get(sourcePath);
    invariant(entry && ['100644', '100755'].includes(entry.mode), `missing or non-regular export input: ${sourcePath}`);
    const bytes = execFileSync('git', ['--no-replace-objects', '-C', source, 'cat-file', 'blob', entry.oid], { timeout: 30000, maxBuffer: 1024 * 1024, env: { ...process.env, GIT_NO_REPLACE_OBJECTS: '1' } });
    total += bytes.length; invariant(bytes.length > 0 && total <= 8 * 1024 * 1024, 'export size budget exceeded');
    files.push({ path: name, bytes, sourcePath, gitBlob: entry.oid });
  }
  files.push({ path: 'README.md', bytes: Buffer.from(README), sourcePath: null, gitBlob: null });
  files.push({ path: 'package.json', bytes: Buffer.from(JSON.stringify({ name: 'portable-ci-continuation', version: '0.1.0', private: true, type: 'module', engines: { node: '>=22' }, scripts: { test: 'node --test', demo: 'node examples/demo.mjs' } }, null, 2) + '\n'), sourcePath: null, gitBlob: null });
  files.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  const manifest = { format: 'ci.portable-export.v1', authority: 'checksums-only', sourceCommit: state.commit, sourceTree: state.tree,
    files: files.map(({ path, bytes, sourcePath, gitBlob }) => ({ path, bytes: bytes.length, sha256: sha256(bytes), sourcePath, gitBlob })) };
  mkdirSync(target, { mode: 0o700 });
  try {
    for (const file of files) { const path = resolve(target, file.path); mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, file.bytes, { flag: 'wx', mode: 0o600 }); }
    writeFileSync(resolve(target, 'export-manifest.json'), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  } catch (error) { rmSync(target, { recursive: true, force: true }); throw error; }
  return { directory: target, manifest };
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const o = options(process.argv.slice(2), ['--repo', '--commit', '--out']);
    for (const key of ['--repo', '--commit', '--out']) invariant(o[key], `missing ${key}`);
    const r = exportKit({ repo: o['--repo'], commit: o['--commit'], out: o['--out'] });
    console.log(JSON.stringify({ directory: r.directory, sourceCommit: r.manifest.sourceCommit, files: r.manifest.files.length, authority: r.manifest.authority }, null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
