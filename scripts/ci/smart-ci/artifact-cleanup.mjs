#!/usr/bin/env node
// CI-09 (#2333, tracker #2324): inventory — and, only on explicit maintainer confirmation,
// delete — workflow artifacts that no longer have a retention reason.
//
//   node scripts/ci/smart-ci/artifact-cleanup.mjs [--repo owner/name] [--name-prefix container-image-artifacts]
//        [--older-than-days 7] [--json report.json]                    # DRY RUN (default): lists candidates + bytes
//   node scripts/ci/smart-ci/artifact-cleanup.mjs ... --delete --ids-file report.json --confirm-count <N>
//
// The dry run is the deliverable an agent may produce. Deletion is destructive and is a
// maintainer action (the SC-2 row of OUTSTANDING_TASKS.md, recorded on #2333): it runs only with
// --delete AND an explicit scope (--name-prefix, or --all) AND an --ids-file written by a dry run
// AND a --confirm-count equal to the number of those ids that are still present, unexpired and
// matching in a fresh listing — so a listing that drifted underneath the operator can never
// delete more, or other, artifacts than the ones the dry run displayed. Artifacts produced on
// main/master or a release tag (nightly and release evidence) are never candidates unless
// --include-protected is passed. A truncated listing is refused in every mode.
// Token: GH_TOKEN / GITHUB_TOKEN or `gh auth token`; deletion needs `actions: write`.

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

const API = 'https://api.github.com';
const DELETE_SPACING_MS = 250;
export const PROTECTED_BRANCHES = Object.freeze(['main', 'master']);
export const PROTECTED_BRANCH_PATTERN = /^v\d+\.\d+/;

/**
 * Pure selection over the artifacts listing (items of `artifacts`).
 * Artifacts produced by runs on a protected branch (main/master) or a release tag are never
 * candidates — that is nightly and release evidence with its own retention class — and neither
 * is an artifact whose originating run is unknown.
 */
export function selectArtifacts(artifacts, { namePrefix = null, olderThanDays = 0, now = Date.now(), ids = null, includeProtected = false } = {}) {
  const cutoff = now - olderThanDays * 86400000;
  const allowed = ids ? new Set(ids.map(Number)) : null;
  let protectedCount = 0;
  let unknownOrigin = 0;
  const selected = artifacts.filter((artifact) => {
    if (artifact.expired) return false;
    if (allowed && !allowed.has(Number(artifact.id))) return false;
    if (namePrefix && !String(artifact.name ?? '').startsWith(namePrefix)) return false;
    const created = Date.parse(artifact.created_at ?? '');
    if (Number.isNaN(created) || created > cutoff) return false;
    if (!includeProtected) {
      const branch = artifact.workflow_run && typeof artifact.workflow_run.head_branch === 'string' ? artifact.workflow_run.head_branch : null;
      if (branch === null) {
        unknownOrigin += 1;
        return false;
      }
      if (PROTECTED_BRANCHES.includes(branch) || PROTECTED_BRANCH_PATTERN.test(branch)) {
        protectedCount += 1;
        return false;
      }
    }
    return true;
  });
  const bytes = selected.reduce((sum, artifact) => sum + (artifact.size_in_bytes ?? 0), 0);
  return { selected, count: selected.length, bytes, gb: Number((bytes / 1e9).toFixed(3)), excluded: { protectedBranchOrTag: protectedCount, unknownOrigin } };
}

/** The deletion guard, pure so it can be tested: returns the reason deletion must not proceed, or null. */
export function assertDeletable({ truncated, confirmCount, count, idsFileCount, namePrefix = null, all = false }) {
  if (truncated) return 'the listing was truncated at the page cap; refusing to delete from a partial view';
  if (!namePrefix && !all) return 'delete mode needs an explicit scope: --name-prefix <prefix>, or --all to acknowledge every matching artifact';
  if (!Number.isInteger(idsFileCount) || idsFileCount === 0) return 'no --ids-file from a dry run was supplied (or it lists no ids)';
  if (!Number.isInteger(confirmCount)) return '--confirm-count <N> is required';
  if (confirmCount !== count) return `--confirm-count ${confirmCount} does not match the ${count} listed ids still present and matching; aborting without deleting`;
  if (count === 0) return 'nothing to delete';
  return null;
}

export function parseArgs(argv) {
  const args = { repo: 'Chris0Jeky/Taskdeck', namePrefix: null, olderThanDays: 7, json: null, delete: false, confirmCount: null, idsFile: null, maxPages: 1000, all: false, includeProtected: false };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--repo': args.repo = next(); break;
      case '--name-prefix': args.namePrefix = next(); break;
      case '--older-than-days': args.olderThanDays = Number(next()); break;
      case '--json': args.json = next(); break;
      case '--delete': args.delete = true; break;
      case '--confirm-count': args.confirmCount = Number(next()); break;
      case '--ids-file': args.idsFile = next(); break;
      case '--all': args.all = true; break;
      case '--include-protected': args.includeProtected = true; break;
      case '--max-pages': args.maxPages = Number(next()); break;
      case '--help':
        console.log('usage: artifact-cleanup.mjs [--repo owner/name] [--name-prefix P | --all] [--older-than-days N] [--include-protected] [--json FILE] [--delete --ids-file FILE --confirm-count N]');
        process.exit(0);
        break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  if (!/^[\w.-]+\/[\w.-]+$/.test(args.repo)) throw new Error('--repo must be owner/name');
  if (!Number.isFinite(args.olderThanDays) || args.olderThanDays < 0) throw new Error('--older-than-days must be a number >= 0');
  if (!Number.isInteger(args.maxPages) || args.maxPages < 1) throw new Error('--max-pages must be a positive integer');
  if (args.delete && !args.idsFile) throw new Error('--delete requires --ids-file <report.json> written by a dry run');
  if (args.delete && !Number.isInteger(args.confirmCount)) throw new Error('--delete requires --confirm-count <N> equal to the listed candidate count');
  if (args.delete && !args.namePrefix && !args.all) throw new Error('--delete requires --name-prefix <prefix> or --all (explicit acknowledgement that every matching artifact is in scope)');
  return args;
}

function resolveToken() {
  const fromEnv = process.env.GH_TOKEN || process.env.GITHUB_TOKEN;
  if (fromEnv) return fromEnv;
  try {
    return execFileSync('gh', ['auth', 'token'], { encoding: 'utf8' }).trim();
  } catch {
    throw new Error('No token: set GH_TOKEN/GITHUB_TOKEN or authenticate `gh`');
  }
}

async function sleep(ms) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

async function ghRequest(token, url, method = 'GET') {
  for (let attempt = 0; attempt < 6; attempt += 1) {
    const response = await fetch(url, {
      method,
      headers: { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'taskdeck-smart-ci-artifact-cleanup' },
    });
    if (response.status === 403 || response.status === 429) {
      const retryAfter = Number(response.headers.get('retry-after') ?? '0') * 1000;
      const remaining = Number(response.headers.get('x-ratelimit-remaining') ?? '1');
      const reset = Number(response.headers.get('x-ratelimit-reset') ?? '0') * 1000;
      const waitMs = retryAfter || (remaining === 0 && reset ? Math.max(1000, reset - Date.now() + 1000) : 15000 * (attempt + 1));
      console.error(`rate limited (${response.status}) on ${method}; waiting ${Math.round(waitMs / 1000)}s`);
      await sleep(Math.min(waitMs, 15 * 60 * 1000));
      continue;
    }
    if (response.status >= 500) {
      await sleep(2000 * (attempt + 1));
      continue;
    }
    if (response.status === 204) return { status: 204, json: null, link: '' };
    if (!response.ok) {
      const error = new Error(`GitHub API ${response.status} ${method} ${url}: ${await response.text()}`);
      error.status = response.status;
      throw error;
    }
    return { status: response.status, json: await response.json(), link: response.headers.get('link') ?? '' };
  }
  throw new Error(`GitHub API gave up after retries: ${method} ${url}`);
}

export function nextLink(linkHeader) {
  const match = /<([^>]+)>;\s*rel="next"/.exec(linkHeader ?? '');
  return match ? match[1] : null;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const token = resolveToken();
  const base = `${API}/repos/${args.repo}`;
  const artifacts = [];
  let url = `${base}/actions/artifacts?per_page=100`;
  let pages = 0;
  while (url && pages < args.maxPages) {
    const { json, link } = await ghRequest(token, url);
    artifacts.push(...(json.artifacts ?? []));
    pages += 1;
    url = nextLink(link);
  }
  const truncated = Boolean(url);
  let idsFromFile = null;
  if (args.idsFile) {
    if (!existsSync(args.idsFile)) throw new Error(`--ids-file ${args.idsFile} does not exist`);
    const parsed = JSON.parse(readFileSync(args.idsFile, 'utf8'));
    idsFromFile = Array.isArray(parsed.ids) ? parsed.ids.map(Number).filter(Number.isInteger) : [];
  }
  const selection = selectArtifacts(artifacts, { namePrefix: args.namePrefix, olderThanDays: args.olderThanDays, ids: idsFromFile, includeProtected: args.includeProtected });
  const report = {
    repository: args.repo,
    generatedAtUtc: new Date().toISOString(),
    listed: artifacts.length,
    truncated,
    filter: { namePrefix: args.namePrefix, olderThanDays: args.olderThanDays, idsFile: args.idsFile, includeProtected: args.includeProtected },
    excluded: selection.excluded,
    candidates: selection.count,
    bytes: selection.bytes,
    gb: selection.gb,
    mode: args.delete ? 'delete' : 'dry-run',
  };
  if (args.json) writeFileSync(args.json, `${JSON.stringify({ ...report, ids: selection.selected.map((artifact) => artifact.id) }, null, 2)}\n`);
  console.log(`${report.mode}: ${report.candidates} candidate artifact(s) = ${report.gb} GB (listed ${report.listed}${truncated ? ', TRUNCATED at the page cap' : ''}; filter ${JSON.stringify(report.filter)}; excluded ${selection.excluded.protectedBranchOrTag} from main/master/tag runs and ${selection.excluded.unknownOrigin} of unknown origin)`);
  if (!args.delete) {
    if (truncated) {
      console.error('The listing hit the page cap; the totals above are a lower bound. Raise --max-pages and re-run before quoting them.');
      process.exit(2);
    }
    console.log(`Nothing deleted. To delete exactly these, re-run with --delete --ids-file ${args.json ?? '<the --json report>'} --confirm-count ${report.candidates}.`);
    return;
  }
  const refusal = assertDeletable({ truncated, confirmCount: args.confirmCount, count: selection.count, idsFileCount: idsFromFile ? idsFromFile.length : null, namePrefix: args.namePrefix, all: args.all });
  if (refusal) throw new Error(refusal);
  let deleted = 0;
  let alreadyGone = 0;
  const failed = [];
  try {
    for (const artifact of selection.selected) {
      try {
        await ghRequest(token, `${base}/actions/artifacts/${artifact.id}`, 'DELETE');
        deleted += 1;
      } catch (error) {
        if (error && error.status === 404) alreadyGone += 1;
        else failed.push({ id: artifact.id, error: String(error.message ?? error).slice(0, 200) });
      }
      if ((deleted + alreadyGone + failed.length) % 100 === 0) console.error(`progress: ${deleted} deleted, ${alreadyGone} already gone, ${failed.length} failed of ${selection.count}`);
      await sleep(DELETE_SPACING_MS);
    }
  } finally {
    console.log(`deleted ${deleted} of ${selection.count} artifact(s) (${alreadyGone} already gone, ${failed.length} failed) = up to ${selection.gb} GB`);
    for (const failure of failed.slice(0, 20)) console.error(`  failed ${failure.id}: ${failure.error}`);
  }
  if (failed.length > 0) process.exit(1);
}

if (process.argv[1] && /artifact-cleanup\.mjs$/.test(process.argv[1])) {
  main().catch((error) => {
    console.error(error.stack ?? String(error));
    process.exit(1);
  });
}
