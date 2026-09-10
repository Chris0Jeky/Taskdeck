#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { lstatSync, readFileSync, readdirSync, realpathSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

// Self-contained: run from independently trusted tooling, never from an untrusted export.
const invariant = (condition, message) => { if (!condition) throw new Error(message); };
const isGitId = value => typeof value === 'string' && /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/.test(value);
const validPath = value => typeof value === 'string' && value.length > 0 && !value.startsWith('/') &&
  !/[\\\x00-\x1f\x7f]/.test(value) && !/^[A-Za-z]:/.test(value) &&
  value.split('/').every(part => part !== '' && part !== '.' && part !== '..');

/** Integrity against an unsigned manifest, NOT source authenticity or CI qualification. */
export function verifyExport(directory) {
  const root = realpathSync(directory), path = join(root, 'export-manifest.json'), stat = lstatSync(path);
  invariant(stat.isFile() && !stat.isSymbolicLink() && stat.size <= 1024 * 1024, 'invalid export manifest');
  const manifest = JSON.parse(readFileSync(path, 'utf8'));
  invariant(manifest.format === 'ci.portable-export.v1' && manifest.authority === 'checksums-only' && isGitId(manifest.sourceCommit) && isGitId(manifest.sourceTree), 'invalid manifest identity');
  invariant(Array.isArray(manifest.files) && manifest.files.length > 0 && manifest.files.length <= 1000, 'invalid manifest inventory');
  const seen = new Set(); let total = 0;
  for (const file of manifest.files) {
    invariant(validPath(file.path) && file.path !== 'export-manifest.json' && !seen.has(file.path), 'duplicate or unsafe export path'); seen.add(file.path);
    invariant(Number.isSafeInteger(file.bytes) && file.bytes > 0 && file.bytes <= 1024 * 1024 && /^[a-f0-9]{64}$/.test(file.sha256), 'invalid file metadata');
    // Reject links in every relative path component before reading any file.
    let current = root;
    for (const segment of file.path.split('/')) { current = join(current, segment); invariant(!lstatSync(current).isSymbolicLink(), 'export contains a symlink'); }
    const s = lstatSync(current); invariant(s.isFile() && s.size === file.bytes, 'export file length mismatch');
    total += s.size; invariant(total <= 8 * 1024 * 1024, 'export exceeds budget');
    const bytes = readFileSync(current); invariant(createHash('sha256').update(bytes).digest('hex') === file.sha256, 'export checksum mismatch');
  }
  const actual = []; let entries = 0;
  function walk(dir, prefix = '', depth = 0) {
    invariant(depth <= 32, 'export directory depth exceeds budget');
    for (const name of readdirSync(dir)) {
      invariant(++entries <= 2000, 'export entry budget exceeded');
      const s = lstatSync(join(dir, name)), p = prefix + name;
      invariant(!s.isSymbolicLink(), 'export contains a symlink');
      if (s.isDirectory()) walk(join(dir, name), p + '/', depth + 1); else { invariant(s.isFile(), 'export contains a special file'); actual.push(p); }
      invariant(actual.length <= 1001, 'export inventory exceeds budget');
    }
  }
  walk(root);
  invariant(actual.length === seen.size + 1 && actual.every(p => p === 'export-manifest.json' || seen.has(p)), 'unexpected export files');
  return { valid: true, authority: 'checksums-only', sourceCommit: manifest.sourceCommit, files: seen.size };
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const args = process.argv.slice(2);
    invariant(args.length === 2 && args[0] === '--dir' && args[1] && !args[1].startsWith('--'), 'usage: trusted-verify-export.mjs --dir DIRECTORY');
    console.log(JSON.stringify(verifyExport(args[1]), null, 2));
  }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
