import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync, symlinkSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { exportKit, KIT_PREFIX, PORTABLE_FILES } from '../tools/export-kit.mjs';
import { verifyExport } from '../tools/verify-export.mjs';

const kit = fileURLToPath(new URL('../', import.meta.url));
function fixture(fn) {
  const dir = mkdtempSync(join(tmpdir(), 'ci-portable-')), repo = join(dir, 'repo'); mkdirSync(repo);
  const git = (...args) => execFileSync('git', ['-C', repo, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    git('init'); git('config', 'user.name', 'Portable fixture'); git('config', 'user.email', 'fixture@example.invalid');
    for (const name of PORTABLE_FILES) { const out = join(repo, KIT_PREFIX, name); mkdirSync(dirname(out), { recursive: true }); writeFileSync(out, readFileSync(join(kit, name))); }
    writeFileSync(join(repo, 'LICENSE'), 'TEST FIXTURE LICENSE: not a redistributed product.\n');
    git('add', '.'); git('commit', '-m', 'fixture kit'); const commit = git('rev-parse', 'HEAD');
    return fn({ dir, repo, commit, git, out: join(dir, 'portable') });
  } finally { rmSync(dir, { recursive: true, force: true }); }
}
test('immutable export is reproducible and ignores dirty source files', () => fixture(f => {
  const first = exportKit(f); writeFileSync(join(f.repo, KIT_PREFIX, 'core/primitives.mjs'), 'untrusted dirty file');
  const second = exportKit({ ...f, out: join(f.dir, 'portable-2') }); assert.deepEqual(first.manifest, second.manifest);
  assert.equal(readFileSync(join(first.directory, 'LICENSE'), 'utf8'), readFileSync(join(f.repo, 'LICENSE'), 'utf8'));
  assert.ok(verifyExport(first.directory).valid); assert.ok(!existsSync(join(first.directory, 'adapters/taskdeck.mjs')));
}));
test('export refuses source destinations, existing output and symbolic commit refs', () => fixture(f => {
  assert.throws(() => exportKit({ ...f, out: join(f.repo, 'export') }));
  exportKit(f); assert.throws(() => exportKit(f)); assert.throws(() => exportKit({ ...f, commit: 'HEAD', out: join(f.dir, 'other') }));
}));
test('missing and symlink source files fail before creating output', () => fixture(f => {
  rmSync(join(f.repo, KIT_PREFIX, 'core/primitives.mjs')); gitCommit(f, 'remove');
  assert.throws(() => exportKit({ ...f, commit: f.git('rev-parse', 'HEAD') })); assert.ok(!existsSync(f.out));
  // Git symlink object: no Windows symlink privilege required.
  const blob = execFileSync('git', ['-C', f.repo, 'hash-object', '-w', '--stdin'], { input: '../../../../../../LICENSE', encoding: 'utf8' }).trim();
  f.git('update-index', '--add', '--cacheinfo', `120000,${blob},${KIT_PREFIX}core/primitives.mjs`); f.git('commit', '-m', 'link');
  assert.throws(() => exportKit({ ...f, commit: f.git('rev-parse', 'HEAD') })); assert.ok(!existsSync(f.out));
}));
function gitCommit(f, message) { f.git('add', '-A'); f.git('commit', '-m', message); }
test('verification rejects tampering, extra files and output symlinks', () => fixture(f => {
  exportKit(f); const path = join(f.out, 'cli.mjs'), original = readFileSync(path);
  writeFileSync(path, 'changed'); assert.throws(() => verifyExport(f.out)); writeFileSync(path, original);
  writeFileSync(join(f.out, 'unexpected'), 'x'); assert.throws(() => verifyExport(f.out)); rmSync(join(f.out, 'unexpected'));
  // A junction tests linked path components without elevation on Windows.
  rmSync(join(f.out, 'core'), { recursive: true });
  symlinkSync(join(f.repo, KIT_PREFIX, 'core'), join(f.out, 'core'), process.platform === 'win32' ? 'junction' : 'dir');
  assert.throws(() => verifyExport(f.out));
}));

test('trusted standalone verifier never executes altered export modules', () => fixture(f => {
  exportKit(f);
  const marker = join(f.dir, 'executed'), trusted = join(f.dir, 'trusted-verifier.mjs');
  writeFileSync(trusted, readFileSync(join(kit, 'tools/verify-export.mjs')));
  const malicious = `import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(marker)}, 'executed');`;
  for (const name of ['core/primitives.mjs', 'cli.mjs', 'tools/verify-export.mjs']) writeFileSync(join(f.out, name), malicious);
  assert.throws(() => execFileSync(process.execPath, [trusted, '--dir', f.out], { stdio: 'pipe' }));
  assert.equal(existsSync(marker), false);
}));
test('exported provider-neutral regression suite runs without Taskdeck files', () => fixture(f => {
  exportKit(f);
  // The nested suite is a separate process, not a child in this runner's IPC protocol.
  const env = { ...process.env }; delete env.NODE_TEST_CONTEXT;
  const output = execFileSync(process.execPath, ['--test', '--test-reporter=tap'], { cwd: f.out, env, encoding: 'utf8', timeout: 30000, maxBuffer: 8 * 1024 * 1024 });
  assert.match(output, /# fail 0\b/); assert.match(output, /# cancelled 0\b/); assert.match(output, /# skipped 0\b/);
  const result = JSON.parse(execFileSync(process.execPath, ['tools/verify-export.mjs', '--dir', '.'], { cwd: f.out, encoding: 'utf8' }));
  assert.ok(result.valid); assert.equal(result.authority, 'checksums-only');
}));
