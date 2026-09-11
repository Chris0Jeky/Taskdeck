import { invariant, isGitId } from '../core/primitives.mjs';

export const API_VERSION = '2026-03-10';
export function numericId(value) {
  invariant(typeof value === 'string' && /^[1-9]\d*$/.test(value) || Number.isSafeInteger(value) && value > 0, 'invalid numeric provider ID');
  return String(value);
}
export function repositoryName(value) {
  invariant(typeof value === 'string' && /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(value) && value.split('/').every(p => !['.', '..'].includes(p)), 'invalid repository name');
  return value;
}
function bounded(value, limit, label) { invariant(typeof value === 'string' && value.length <= limit, `invalid ${label}`); return value; }
const time = value => typeof value === 'string' && Number.isFinite(Date.parse(value)) && Date.parse(value) >= 0 ? Date.parse(value) : null;
const duration = (start, end) => time(start) !== null && time(end) !== null && time(end) >= time(start) ? (time(end) - time(start)) / 1000 : null;

/** Only fixed-origin GET endpoints. Redirects and unbounded body/page/attempt reads are forbidden. */
export function createGitHubReader({ token, fetchImpl = globalThis.fetch, maxRequests = 100, maxBytes = 8 * 1024 * 1024, maxTotalBytes = 64 * 1024 * 1024, timeoutMs = 15000 }) {
  invariant(typeof token === 'string' && token.length > 0 && !/[\r\n]/.test(token), 'authenticated read token required');
  invariant(typeof fetchImpl === 'function', 'fetch implementation required');
  for (const [value, cap] of [[maxRequests, 1000], [maxBytes, 32 * 1024 * 1024], [maxTotalBytes, 256 * 1024 * 1024], [timeoutMs, 60000]]) invariant(Number.isSafeInteger(value) && value > 0 && value <= cap, 'invalid reader budget');
  let requests = 0, totalBytes = 0;
  async function get(path) {
    invariant(++requests <= maxRequests, 'GitHub request budget exceeded');
    let response;
    try {
      response = await fetchImpl(`https://api.github.com${path}`, { method: 'GET', redirect: 'error', signal: AbortSignal.timeout(timeoutMs),
        headers: { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'X-GitHub-Api-Version': API_VERSION } });
    } catch { throw new Error('GitHub transport failure (no response accepted)'); }
    invariant(response.status === 200, `GitHub read failed (HTTP ${response.status})`);
    const length = response.headers.get('content-length');
    invariant(length === null || /^\d+$/.test(length) && Number(length) <= maxBytes, 'GitHub body exceeds budget');
    invariant(response.body, 'GitHub response has no body');
    const reader = response.body.getReader(), chunks = []; let bytes = 0;
    try {
      while (true) {
        const { value, done } = await reader.read(); if (done) break;
        bytes += value.byteLength; totalBytes += value.byteLength;
        invariant(bytes <= maxBytes && totalBytes <= maxTotalBytes, 'GitHub body exceeds budget'); chunks.push(value);
      }
      return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
    } catch { throw new Error('GitHub response is oversized, interrupted or invalid JSON'); }
    finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
  }
  const prefix = repo => `/repos/${repositoryName(repo)}`;
  return {
    repository: repo => get(prefix(repo)),
    workflow: (repo, id) => get(`${prefix(repo)}/actions/workflows/${numericId(id)}`),
    run: (repo, id, attempt = null) => get(`${prefix(repo)}/actions/runs/${numericId(id)}${attempt === null ? '' : `/attempts/${numericId(attempt)}`}`),
    jobs: (repo, id, attempt, page) => get(`${prefix(repo)}/actions/runs/${numericId(id)}/attempts/${numericId(attempt)}/jobs?per_page=100&page=${numericId(page)}`),
    requestCount: () => requests, byteCount: () => totalBytes
  };
}
function jobRecord(job, run, attempt) {
  invariant(numericId(job.run_id) === numericId(run.id) && job.head_sha === run.head_sha, 'job/run binding mismatch');
  if (job.run_attempt !== undefined) invariant(job.run_attempt === attempt, 'job attempt mismatch');
  const steps = job.steps ?? [];
  invariant(Array.isArray(steps) && steps.length <= 1000, 'invalid step inventory');
  const numbers = new Set();
  const normalizedSteps = steps.map(step => {
    invariant(Number.isSafeInteger(step.number) && step.number > 0 && !numbers.has(step.number), 'duplicate/invalid step number'); numbers.add(step.number);
    return { number: step.number, name: bounded(step.name, 2048, 'step name'), status: bounded(step.status, 100, 'step status'),
      conclusion: step.conclusion === null ? null : bounded(step.conclusion, 100, 'step conclusion'), seconds: duration(step.started_at, step.completed_at) };
  });
  return { jobId: numericId(job.id), attempt, name: bounded(job.name, 2048, 'job name'),
    status: bounded(job.status, 100, 'job status'), conclusion: job.conclusion === null ? null : bounded(job.conclusion, 100, 'job conclusion'),
    startedAt: time(job.started_at), completedAt: time(job.completed_at), seconds: duration(job.started_at, job.completed_at),
    queueSeconds: duration(job.created_at, job.started_at), setupSeconds: null, testSeconds: null, executedTests: null,
    steps: normalizedSteps, checkoutVerified: false, eligibility: 'observation-only' };
}
export function summarizeJobs(jobs) {
  const measured = jobs.filter(j => j.seconds !== null), unknown = jobs.filter(j => j.seconds === null && j.conclusion !== 'skipped');
  const executed = jobs.filter(j => j.conclusion !== 'skipped');
  const starts = executed.map(j => j.startedAt).filter(t => t !== null), ends = executed.map(j => j.completedAt).filter(t => t !== null);
  const knownRunnerSeconds = measured.reduce((sum, j) => sum + j.seconds, 0);
  const complete = jobs.length > 0 && unknown.length === 0;
  return { jobs: jobs.length, knownRunnerSeconds, unknownDurationJobs: unknown.length,
    aggregateRunnerSeconds: complete ? knownRunnerSeconds : null,
    jobSpanSeconds: complete && starts.length && ends.length ? (Math.max(...ends) - Math.min(...starts)) / 1000 : null,
    conclusions: Object.fromEntries([...new Set(jobs.map(j => j.conclusion ?? 'unknown'))].sort().map(c => [c, jobs.filter(j => (j.conclusion ?? 'unknown') === c).length])),
    billingEstimate: null, setupSeconds: null, testSeconds: null, executedTests: null };
}

/** Authenticated metadata collection, NOT an issuer of reusable task attestations. */
export async function collectRun({ reader, repository, repositoryId, runId, workflowId, workflowPath, expectedHead = null, maxAttempts = 10, maxPages = 50, now = Math.floor(Date.now() / 1000) }) {
  repositoryName(repository); [repositoryId, runId, workflowId].forEach(numericId);
  invariant(typeof workflowPath === 'string' && /^\.github\/workflows\/[A-Za-z0-9_.-]+\.ya?ml$/.test(workflowPath), 'invalid workflow path');
  invariant(Number.isSafeInteger(now) && now >= 0, 'invalid observation clock');
  invariant(Number.isSafeInteger(maxAttempts) && maxAttempts >= 1 && maxAttempts <= 100 && Number.isSafeInteger(maxPages) && maxPages >= 1 && maxPages <= 100, 'invalid collection budget');
  const repo = await reader.repository(repository);
  invariant(numericId(repo.id) === String(repositoryId) && repo.full_name.toLowerCase() === repository.toLowerCase(), 'repository identity mismatch');
  const workflow = await reader.workflow(repository, workflowId);
  invariant(numericId(workflow.id) === String(workflowId) && workflow.path === workflowPath, 'workflow identity mismatch');
  const latest = await reader.run(repository, runId);
  function bind(run) {
    invariant(numericId(run.id) === String(runId) && numericId(run.repository?.id) === String(repositoryId) && numericId(run.workflow_id) === String(workflowId), 'run identity mismatch');
    invariant(run.path === workflowPath && isGitId(run.head_sha) && run.status === 'completed', 'unbound or nonterminal run');
    bounded(run.event, 100, 'event'); bounded(run.conclusion, 100, 'run conclusion');
    invariant(run.head_sha === latest.head_sha && run.event === latest.event, 'attempt identity drift');
    if (expectedHead !== null) invariant(run.head_sha === expectedHead, 'event head mismatch');
  }
  bind(latest);
  invariant(Number.isSafeInteger(latest.run_attempt) && latest.run_attempt >= 1 && latest.run_attempt <= maxAttempts, 'attempt budget exceeded');
  const attempts = [];
  for (let number = 1; number <= latest.run_attempt; number++) {
    const run = await reader.run(repository, runId, number); bind(run);
    invariant(run.run_attempt === number, 'attempt binding mismatch');
    const jobs = [], seen = new Set(); let count = null;
    for (let page = 1; page <= maxPages; page++) {
      const body = await reader.jobs(repository, runId, number, page);
      invariant(Number.isSafeInteger(body.total_count) && body.total_count >= 0 && body.total_count <= maxPages * 100, 'job budget/count invalid');
      invariant(count === null || count === body.total_count, 'job inventory changed during pagination'); count = body.total_count;
      invariant(Array.isArray(body.jobs) && body.jobs.length <= 100 && (body.jobs.length > 0 || count === 0), 'truncated job inventory');
      for (const job of body.jobs) {
        const normalized = jobRecord(job, run, number); invariant(!seen.has(normalized.jobId), 'duplicate job in attempt'); seen.add(normalized.jobId); jobs.push(normalized);
      }
      invariant(jobs.length <= count, 'job count overrun');
      if (jobs.length === count) break;
      invariant(body.jobs.length === 100 && page < maxPages, 'incomplete job pagination');
    }
    invariant(count !== null && jobs.length === count, 'incomplete job inventory');
    attempts.push({ attempt: number, conclusion: run.conclusion, jobs, summary: summarizeJobs(jobs) });
  }
  const after = await reader.run(repository, runId); bind(after);
  invariant(after.run_attempt === latest.run_attempt && after.conclusion === latest.conclusion, 'run changed during collection');
  return { format: 'ci.github-observation.v1', authority: 'none', complete: true, observedAt: now,
    repositoryId: String(repositoryId), repository, workflowId: String(workflowId), workflowPath, runId: String(runId),
    event: latest.event, headSha: latest.head_sha, latestAttempt: latest.run_attempt, attempts,
    checkoutVerified: false, requests: reader.requestCount(), responseBytes: reader.byteCount(),
    limitations: ['REST head_sha identifies run metadata, not the actual checked-out merge commit/tree.',
      'Workflow/job names and successful REST conclusions do not prove command, environment or complete test execution.',
      'Unknown timing/test/billing values remain null. Job span is not a dependency-graph critical-path computation.',
      'Observation only: never sign or reuse a task based on this report.'] };
}
function escapeMarkdown(value) {
  return String(value).replace(/[\\[\]()!#*_]/g, '\\$&').replace(/[&<>|`@\r\n]/g,
    c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '|': '&#124;', '`': '&#96;', '@': '&#64;', '\r': ' ', '\n': ' ' })[c]);
}
export function renderObservation(report) {
  invariant(report?.format === 'ci.github-observation.v1' && report.complete === true, 'complete observation required');
  const lines = ['## CI execution observation', '', `Run ${report.runId}; ${report.attempts.length} attempt(s). Read-only metadata, not reusable proof.`, '',
    '| Attempt | Run conclusion | Jobs | Known runner seconds | Unknown durations |', '| --- | --- | ---: | ---: | ---: |'];
  for (const a of report.attempts) lines.push(`| ${a.attempt} | ${escapeMarkdown(a.conclusion)} | ${a.summary.jobs} | ${a.summary.knownRunnerSeconds} | ${a.summary.unknownDurationJobs} |`);
  const failures = report.attempts.flatMap(a => a.jobs.filter(j => !['success', 'skipped'].includes(j.conclusion)).map(j => ({ ...j, attempt: a.attempt })));
  lines.push('', '### Non-successful jobs (all attempts retained)', '');
  for (const j of failures.slice(0, 30)) lines.push(`- Attempt ${j.attempt}: ${escapeMarkdown(j.name)} — ${escapeMarkdown(j.conclusion ?? j.status)}.`);
  if (!failures.length) lines.push('No non-successful job conclusions in the collected metadata. This is not a test-completeness claim.');
  if (failures.length > 30) lines.push(`Additional ${failures.length - 30} entries are retained in JSON.`);
  lines.push('', 'Known seconds exclude unknown durations and are not billed minutes. Checkout/command/environment/test provenance is not established.');
  return lines.join('\n') + '\n';
}
