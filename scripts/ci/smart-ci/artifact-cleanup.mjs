#!/usr/bin/env node
// CI-09 (#2333): inventory — and, only on explicit maintainer confirmation, delete — workflow
// artifacts that no longer have a retention reason.
//
//   node scripts/ci/smart-ci/artifact-cleanup.mjs [--repo owner/name] [--name-prefix container-image-artifacts]
//        [--older-than-days 0] [--json out.json]                        # DRY RUN (default): lists candidates + bytes
//   node scripts/ci/smart-ci/artifact-cleanup.mjs ... --delete --confirm-count <N>   # deletes exactly the N listed
//
// The dry run is the deliverable an agent may produce. Deletion is destructive and is a
// maintainer action (OUTSTANDING_TASKS.md §J SC-2): it runs only with --delete AND a
// --confirm-count equal to the number of candidates the same invocation listed, so a
// listing that changed underneath the operator aborts instead of deleting more.
// Token: GH_TOKEN / GITHUB_TOKEN or `gh auth token`; deletion needs `actions: write`.

import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';

const API = 'https://api.github.com';

/** Pure selection over the artifacts listing (items of `artifacts`). */
export function selectArtifacts(artifacts, { namePrefix = null, olderThanDays = 0, now = Date.now() } = {}) {
  const cutoff = now - olderThanDays * 86400000;
  const selected = artifacts.filter((artifact) => {
    if (artifact.expired) return false;
    if (namePrefix && !String(artifact.name ?? '').startsWith(namePrefix)) return false;
    const created = Date.parse(artifact.created_at ?? '');
    if (Number.isNaN(created)) return false;
    return created <= cutoff;
  });
  const bytes = selected.reduce((sum, artifact) => sum + (artifact.size_in_bytes ?? 0), 0);
  return { selected, count: selected.length, bytes, gb: Number((bytes / 1e9).toFixed(3)) };
}

function parseArgs(argv) {
  const args = { repo: 'Chris0Jeky/Taskdeck', namePrefix: null, olderThanDays: 0, json: null, delete: false, confirmCount: null, maxPages: 400 };
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
      case '--max-pages': args.maxPages = Number(next()); break;
      case '--help':
        console.log('usage: artifact-cleanup.mjs [--repo owner/name] [--name-prefix P] [--older-than-days N] [--json FILE] [--delete --confirm-count N]');
        process.exit(0);
        break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  if (!/^[\w.-]+\/[\w.-]+$/.test(args.repo)) throw new Error('--repo must be owner/name');
  if (args.delete && !Number.isInteger(args.confirmCount)) throw new Error('--delete requires --confirm-count <N> equal to the listed candidate count');
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

async function ghRequest(token, url, method = 'GET') {
  const response = await fetch(url, {
    method,
    headers: { Accept: 'application/vnd.github+json', Authorization: `Bearer ${token}`, 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'taskdeck-smart-ci-artifact-cleanup' },
  });
  if (response.status === 204) return { json: null, link: '' };
  if (!response.ok) throw new Error(`GitHub API ${response.status} ${method} ${url}: ${await response.text()}`);
  return { json: await response.json(), link: response.headers.get('link') ?? '' };
}

function nextLink(linkHeader) {
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
  const selection = selectArtifacts(artifacts, { namePrefix: args.namePrefix, olderThanDays: args.olderThanDays });
  const report = {
    repository: args.repo,
    generatedAtUtc: new Date().toISOString(),
    listed: artifacts.length,
    truncated,
    filter: { namePrefix: args.namePrefix, olderThanDays: args.olderThanDays },
    candidates: selection.count,
    bytes: selection.bytes,
    gb: selection.gb,
    mode: args.delete ? 'delete' : 'dry-run',
  };
  if (args.json) writeFileSync(args.json, `${JSON.stringify({ ...report, ids: selection.selected.map((artifact) => artifact.id) }, null, 2)}\n`);
  console.log(`${report.mode}: ${report.candidates} candidate artifact(s) = ${report.gb} GB (listed ${report.listed}${truncated ? ', TRUNCATED at the page cap' : ''}; filter ${JSON.stringify(report.filter)})`);
  if (!args.delete) {
    console.log('Nothing deleted. Re-run with --delete --confirm-count <N> (N must equal the candidate count above) to delete.');
    return;
  }
  if (truncated) throw new Error('Refusing to delete from a truncated listing');
  if (args.confirmCount !== selection.count) throw new Error(`--confirm-count ${args.confirmCount} does not match the ${selection.count} candidates listed; aborting without deleting`);
  let deleted = 0;
  for (const artifact of selection.selected) {
    await ghRequest(token, `${base}/actions/artifacts/${artifact.id}`, 'DELETE');
    deleted += 1;
    if (deleted % 100 === 0) console.error(`deleted ${deleted}/${selection.count}`);
  }
  console.log(`deleted ${deleted} artifact(s) = ${selection.gb} GB`);
}

if (process.argv[1] && /artifact-cleanup\.mjs$/.test(process.argv[1])) {
  main().catch((error) => {
    console.error(error.stack ?? String(error));
    process.exit(1);
  });
}
