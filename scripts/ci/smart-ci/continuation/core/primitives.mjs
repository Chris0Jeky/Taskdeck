import { createHash } from 'node:crypto';

export function invariant(condition, message) {
  if (!condition) throw new Error(message);
}
export const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
export const unique = values => [...new Set(values)].sort(compare);
export const isDigest = value => /^sha256:[a-f0-9]{64}$/.test(value ?? '');
export const isGitId = value => /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/.test(value ?? '');

/** Canonical JSON over JSON values only. No locale-dependent ordering or silent NaN/null coercion. */
export function canonical(value) {
  if (value === null || typeof value === 'boolean' || typeof value === 'string') return JSON.stringify(value);
  if (typeof value === 'number') {
    invariant(Number.isFinite(value), 'non-finite number');
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  invariant(value && typeof value === 'object' && [Object.prototype, null].includes(Object.getPrototypeOf(value)), 'not a JSON value');
  return `{${Object.keys(value).sort(compare).map(key => `${JSON.stringify(key)}:${canonical(value[key])}`).join(',')}}`;
}
export const hash = value => `sha256:${createHash('sha256').update(canonical(value)).digest('hex')}`;

export function validPath(path) {
  return typeof path === 'string' && path.length > 0 && !path.startsWith('/') &&
    !/[\\\x00-\x1f\x7f]/.test(path) && !/^[A-Za-z]:/.test(path) &&
    path.split('/').every(part => part !== '' && part !== '.' && part !== '..');
}
/** Deliberately small, documented glob language: *, ** and ?. No brace/extglob/negation. */
export function glob(pattern) {
  invariant(validPath(pattern) && !/[\[\]{}!]/.test(pattern), `unsupported glob: ${pattern}`);
  let source = '^';
  for (let i = 0; i < pattern.length; i++) {
    const char = pattern[i];
    if (char === '*' && pattern[i + 1] === '*') {
      i++;
      if (pattern[i + 1] === '/') { i++; source += '(?:.*/)?'; }
      else source += '.*';
    } else if (char === '*') source += '[^/]*';
    else if (char === '?') source += '[^/]';
    else source += char.replace(/[.*+?^${}()|\\]/g, '\\$&');
  }
  return new RegExp(`${source}$`, 'u');
}
// Bounded cache stays private so callers cannot mutate a cached RegExp instance.
const globCache = new Map();
export function matches(path, patterns) {
  return patterns.some(pattern => {
    let compiled = globCache.get(pattern);
    if (!compiled) {
      compiled = glob(pattern);
      if (globCache.size >= 512) globCache.delete(globCache.keys().next().value);
      globCache.set(pattern, compiled);
    }
    return compiled.test(path);
  });
}

/** Dependencies, not their consumers; reverse propagation belongs in expandAffected. */
export function closure(id, nodes, visiting = new Set(), done = new Set()) {
  invariant(Object.hasOwn(nodes, id), `unknown dependency: ${id}`);
  invariant(!visiting.has(id), `dependency cycle at ${id}`);
  if (done.has(id)) return done;
  visiting.add(id);
  for (const dep of nodes[id].deps ?? []) closure(dep, nodes, visiting, done);
  visiting.delete(id); done.add(id);
  return done;
}
