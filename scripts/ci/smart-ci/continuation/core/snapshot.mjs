import { execFileSync } from 'node:child_process';
import { TextDecoder } from 'node:util';
import { compare, invariant, isGitId, validPath } from './primitives.mjs';

const decode = bytes => new TextDecoder('utf-8', { fatal: true }).decode(bytes);
/** Parse git ls-tree -rz. Spaces are supported; unusual control-byte names force fallback. */
export function parseTree(bytes) {
  const text = typeof bytes === 'string' ? bytes : decode(bytes);
  invariant(text === '' || text.endsWith('\0'), 'truncated tree inventory');
  const entries = [];
  for (const row of text.split('\0').filter(Boolean)) {
    const tab = row.indexOf('\t');
    invariant(tab >= 0, 'invalid ls-tree row');
    const [mode, type, oid, ...extra] = row.slice(0, tab).split(' ');
    const path = row.slice(tab + 1);
    invariant(extra.length === 0 && isGitId(oid) && validPath(path), 'invalid tree entry');
    invariant(['100644', '100755', '120000', '160000'].includes(mode), `unsupported mode ${mode}`);
    invariant(type === (mode === '160000' ? 'commit' : 'blob'), 'mode/type mismatch');
    entries.push({ path, mode, type, oid });
  }
  entries.sort((a, b) => compare(a.path, b.path));
  invariant(new Set(entries.map(x => x.path)).size === entries.length, 'duplicate tree path');
  return entries;
}
/** Reads object data only. Never executes project tooling or checks out untrusted code. */
export function snapshot(repo, commit) {
  invariant(isGitId(commit), 'snapshot requires an exact full commit ID, not an expression or branch');
  const git = args => execFileSync('git', ['--no-replace-objects', '-C', repo, ...args], {
    maxBuffer: 64 * 1024 * 1024, timeout: 30000,
    env: { ...process.env, GIT_NO_REPLACE_OBJECTS: '1', GIT_NO_LAZY_FETCH: '1', GIT_ALLOW_PROTOCOL: '' }
  });
  const resolved = decode(git(['rev-parse', '--verify', `${commit}^{commit}`])).trim();
  invariant(resolved === commit, 'commit binding mismatch');
  const tree = decode(git(['rev-parse', '--verify', `${commit}^{tree}`])).trim();
  const entries = parseTree(git(['ls-tree', '-rz', '--full-tree', commit]));
  return { commit, tree, complete: true, entries };
}
/** Validate both comparison endpoints; never silently collapse duplicate paths into a Map. */
export function validateSnapshot(state) {
  invariant(state?.complete === true && isGitId(state.commit) && isGitId(state.tree) && Array.isArray(state.entries), 'complete immutable snapshot required');
  const seen = new Set();
  for (const entry of state.entries) {
    invariant(validPath(entry.path) && isGitId(entry.oid), 'invalid snapshot entry');
    invariant(!seen.has(entry.path), 'duplicate snapshot entry'); seen.add(entry.path);
    invariant(['100644', '100755', '120000', '160000'].includes(entry.mode), 'unsupported snapshot mode');
    invariant(entry.type === (entry.mode === '160000' ? 'commit' : 'blob'), 'snapshot mode/type mismatch');
  }
  return state;
}
/** Full endpoint inventories avoid rename-detection thresholds and API path-list truncation. */
export function changedPaths(base, candidate) {
  validateSnapshot(base); validateSnapshot(candidate);
  const before = new Map(base.entries.map(x => [x.path, `${x.mode}:${x.type}:${x.oid}`]));
  const after = new Map(candidate.entries.map(x => [x.path, `${x.mode}:${x.type}:${x.oid}`]));
  return [...new Set([...before.keys(), ...after.keys()])]
    .filter(path => before.get(path) !== after.get(path)).sort(compare);
}
