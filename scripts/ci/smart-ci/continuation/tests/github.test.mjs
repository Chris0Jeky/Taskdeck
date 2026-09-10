import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createGitHubReader, collectRun, renderObservation, summarizeJobs, numericId, repositoryName, API_VERSION } from '../providers/github.mjs';
import { collectCli } from '../tools/collect-github.mjs';
const sha = 'a'.repeat(40), path = '.github/workflows/ci.yml';
const run = (attempt = 2) => ({ id: 9, repository: { id: 1 }, workflow_id: 2, path, head_sha: sha, event: 'pull_request', status: 'completed', conclusion: attempt === 1 ? 'failure' : 'success', run_attempt: attempt });
const job = (id, attempt = 1) => ({ id, run_id: 9, run_attempt: attempt, head_sha: sha, name: `task-${id}`, status: 'completed', conclusion: attempt === 1 ? 'failure' : 'success',
  started_at: '2026-09-10T00:00:00Z', completed_at: '2026-09-10T00:00:10Z', steps: [{ name: 'Run tests', number: 1, status: 'completed', conclusion: 'success', started_at: '2026-09-10T00:00:01Z', completed_at: '2026-09-10T00:00:09Z' }] });
function fixture(mutate = () => {}, budgets = {}) {
  const calls = []; let latestReads = 0;
  const fetchImpl = async (url, options) => {
    calls.push({ url, options }); const u = new URL(url); let body;
    if (u.pathname === '/repos/owner/repo') body = { id: 1, full_name: 'owner/repo' };
    else if (u.pathname.endsWith('/workflows/2')) body = { id: 2, path };
    else if (u.pathname.endsWith('/jobs')) {
      const attempt = Number(u.pathname.match(/attempts\/(\d+)/)[1]), page = Number(u.searchParams.get('page'));
      const all = attempt === 1 ? Array.from({ length: 101 }, (_, i) => job(i + 10, 1)) : [job(1000, 2)];
      body = { total_count: all.length, jobs: all.slice((page - 1) * 100, page * 100) };
    } else if (/attempts\/\d+$/.test(u.pathname)) body = run(Number(u.pathname.split('/').at(-1)));
    else { body = run(); latestReads++; }
    mutate(body, u, latestReads);
    return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  const reader = createGitHubReader({ token: 'unit-test', fetchImpl, ...budgets });
  return { calls, fetchImpl, input: { reader, repository: 'owner/repo', repositoryId: '1', runId: '9', workflowId: '2', workflowPath: path, expectedHead: sha, now: 100 } };
}
test('collector paginates every attempt and preserves failures before a green rerun', async () => {
  const f = fixture(), report = await collectRun(f.input);
  assert.equal(report.attempts[0].jobs.length, 101); assert.equal(report.attempts[1].jobs.length, 1);
  assert.equal(report.attempts[0].conclusion, 'failure'); assert.equal(report.attempts[1].conclusion, 'success');
  assert.equal(report.attempts[0].summary.knownRunnerSeconds, 1010); assert.equal(report.attempts[0].summary.jobSpanSeconds, 10);
  assert.equal(report.authority, 'none'); assert.equal(report.checkoutVerified, false); assert.equal(report.attempts[0].jobs[0].executedTests, null);
  assert.equal(report.attempts[0].jobs[0].queueSeconds, null); assert.ok(f.calls.some(c => c.url.endsWith('page=2')));
  assert.ok(f.calls.every(c => c.options.method === 'GET' && c.options.redirect === 'error' && c.options.headers['X-GitHub-Api-Version'] === API_VERSION));
  assert.ok(f.calls.every(c => new URL(c.url).origin === 'https://api.github.com'));
});
for (const value of ['../repo', 'x/y?secret', 'https://evil/x', 'x/y/z', 'x/%2e']) test(`reject unsafe repository ${value}`, () => assert.throws(() => repositoryName(value)));
for (const value of [0, -1, Number.MAX_SAFE_INTEGER + 1, '1?x', '01', '--1']) test(`reject unsafe provider ID ${value}`, () => assert.throws(() => numericId(value)));
for (const [name, mutate] of Object.entries({
  wrongRepo: (b, u) => { if (u.pathname === '/repos/owner/repo') b.id = 5; },
  wrongWorkflow: (b, u) => { if (u.pathname.endsWith('/workflows/2')) b.path = '.github/workflows/fake.yml'; },
  wrongRun: b => { if (b.workflow_id) b.repository.id = 5; },
  nonterminal: b => { if (b.workflow_id) b.status = 'in_progress'; },
  wrongHead: b => { if (b.workflow_id) b.head_sha = 'b'.repeat(40); },
  wrongAttempt: (b, u) => { if (u.pathname.endsWith('/attempts/1')) b.run_attempt = 2; },
  jobHead: b => { if (b.jobs?.length) b.jobs[0].head_sha = 'b'.repeat(40); },
  jobRun: b => { if (b.jobs?.length) b.jobs[0].run_id = 10; },
  jobAttempt: b => { if (b.jobs?.length) b.jobs[0].run_attempt = 3; },
  duplicate: (b, u) => { if (u.searchParams.get('page') === '2') b.jobs[0].id = 10; },
  shortPage: b => { if (b.jobs?.length === 100) b.jobs.pop(); },
  countDrift: (b, u) => { if (u.searchParams.get('page') === '2') b.total_count++; },
  stepDuplicate: b => { if (b.jobs?.length) b.jobs[0].steps.push(b.jobs[0].steps[0]); },
  newAttemptRace: (b, u, reads) => { if (reads === 2 && b.workflow_id && !u.pathname.includes('/attempts/')) b.run_attempt = 3; }
})) test(`collector rejects ${name}`, async () => { const f = fixture(mutate); await assert.rejects(collectRun(f.input)); });
test('attempt/page/request bounds fail rather than silently truncate', async () => {
  await assert.rejects(collectRun({ ...fixture().input, maxAttempts: 1 }));
  await assert.rejects(collectRun({ ...fixture().input, maxPages: 1 }));
  await assert.rejects(collectRun(fixture(() => {}, { maxRequests: 2 }).input));
});
test('unknown duration remains null rather than zero', async () => {
  const f = fixture(b => { if (b.jobs?.length) b.jobs[0].started_at = null; }), r = await collectRun(f.input);
  assert.equal(r.attempts[0].summary.aggregateRunnerSeconds, null); assert.equal(r.attempts[0].summary.unknownDurationJobs, 2);
});
test('empty observed job inventory is not a measured zero-cost run', () => assert.equal(summarizeJobs([]).aggregateRunnerSeconds, null));
test('Markdown escapes job-supplied links, images, HTML, mentions and delimiters', async () => {
  const f = fixture(b => { if (b.jobs?.length) b.jobs[0].name = '![leak](https://evil) <img> @owner |`\n'; });
  const md = renderObservation(await collectRun(f.input)); assert.ok(!md.includes('![leak]')); assert.ok(!md.includes('<img>')); assert.ok(!md.includes('@owner'));
});
for (const status of [301, 403, 404, 429, 500]) test(`HTTP ${status} is never accepted`, async () => {
  const reader = createGitHubReader({ token: 'sensitive', fetchImpl: async () => new Response('secret response', { status }) });
  await assert.rejects(reader.repository('a/b'), e => !e.message.includes('secret response') && !e.message.includes('sensitive'));
});
test('transport failures do not echo token-containing exception messages', async () => {
  const reader = createGitHubReader({ token: 'sensitive', fetchImpl: async () => { throw new Error('sensitive'); } });
  await assert.rejects(reader.repository('a/b'), e => !e.message.includes('sensitive'));
});
test('invalid JSON and oversized streaming responses fail closed', async () => {
  for (const text of ['not json', JSON.stringify({ data: 'x'.repeat(1000) })]) {
    const reader = createGitHubReader({ token: 't', maxBytes: 100, fetchImpl: async () => new Response(text) });
    await assert.rejects(reader.repository('a/b'));
  }
});
test('aggregate response byte limit is enforced', async () => {
  const reader = createGitHubReader({ token: 't', maxTotalBytes: 3, fetchImpl: async () => new Response('{}') });
  await reader.repository('a/b'); await assert.rejects(reader.repository('a/b'));
});
test('CLI writes a complete metadata report without creating a reusable receipt', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'ci-observe-')), out = join(dir, 'report.json');
  try {
    const args = ['--repository', 'owner/repo', '--repository-id', '1', '--run', '9', '--workflow-id', '2', '--workflow-path', path, '--out', out];
    const report = await collectCli(args, { token: 't', fetchImpl: fixture().fetchImpl });
    assert.equal(report.authority, 'none'); assert.equal(JSON.parse(readFileSync(out)).attempts.length, 2);
    await assert.rejects(collectCli(args, { token: 't', fetchImpl: fixture().fetchImpl }));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
