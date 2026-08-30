#!/usr/bin/env node
// Smart CI planner CLI (ADR-0066, CI-02 #2326).
//
//   node scripts/ci/smart-ci/plan.mjs --policy ci/policy.v1.json \
//     --event "$GITHUB_EVENT_PATH" --changed-files changed.txt \
//     [--event-name pull_request_target] [--execution-mode hosted] \
//     [--merge-tree-sha <sha>] --out artifacts/ci-plan.json [--summary "$GITHUB_STEP_SUMMARY"]
//
// Reads ONLY metadata: the event payload (SHAs, actor login/type/association, labels,
// draft/fork flags) and a changed-file list. It never reads file contents and never
// executes head code. Any failure produces an escalated "error plan" (every lane,
// hosted) and exits 0 — the gate turns `plannerError` into a red result.

import { existsSync, mkdirSync, readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { buildPlan, errorPlan, policyDigest, renderPlanSummary } from './lib/plan.mjs';

function parseArgs(argv) {
  const args = { policy: 'ci/policy.v1.json', event: null, eventName: process.env.GITHUB_EVENT_NAME ?? null, changedFiles: null, executionMode: null, mergeTreeSha: null, out: 'artifacts/ci-plan.json', summary: null, overrides: {} };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--policy': args.policy = next(); break;
      case '--event': args.event = next(); break;
      case '--event-name': args.eventName = next(); break;
      case '--changed-files': args.changedFiles = next(); break;
      case '--execution-mode': args.executionMode = next(); break;
      case '--merge-tree-sha': args.mergeTreeSha = next(); break;
      case '--out': args.out = next(); break;
      case '--summary': args.summary = next(); break;
      // Local what-if planning without an event payload (docs/ci/SMART_CI.md §10):
      case '--base-sha': args.overrides.baseSha = next(); break;
      case '--head-sha': args.overrides.headSha = next(); break;
      case '--repository': args.overrides.repository = next(); break;
      case '--pr': args.overrides.pullRequestNumber = Number(next()); break;
      case '--actor': args.overrides.actorLogin = next(); break;
      case '--association': args.overrides.authorAssociation = next(); break;
      case '--fork': args.overrides.isFork = true; break;
      case '--labels': args.overrides.labels = next().split(',').map((label) => label.trim()).filter(Boolean); break;
      case '--help':
        console.log('usage: plan.mjs --policy <file> (--event <github event json> | --base-sha S --head-sha S [--actor L] [--association A] [--fork] [--labels a,b] [--pr N]) --changed-files <list> [--event-name N] [--execution-mode M] [--merge-tree-sha S] --out <file> [--summary <file>]');
        process.exit(0);
        break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return args;
}

/** Turn a GitHub event payload into the content-free planner input. */
export function inputFromEvent(event, eventName, options = {}) {
  const repository = event && event.repository ? event.repository.full_name ?? null : null;
  const ownerLogin = event && event.repository && event.repository.owner ? event.repository.owner.login ?? null : null;
  const input = {
    eventName,
    repository,
    pullRequestNumber: null,
    ref: null,
    isDraft: false,
    baseSha: null,
    headSha: null,
    mergeSha: null,
    mergeTreeSha: options.mergeTreeSha ?? null,
    actorLogin: null,
    actorType: null,
    authorAssociation: null,
    isFork: false,
    labels: [],
    changedFiles: options.changedFiles ?? [],
    changedFilesAvailable: options.changedFilesAvailable === true,
    executionMode: options.executionMode ?? null,
  };
  const pr = event ? event.pull_request : null;
  if (pr) {
    input.pullRequestNumber = Number.isInteger(pr.number) ? pr.number : null;
    input.isDraft = pr.draft === true;
    input.baseSha = pr.base ? pr.base.sha ?? null : null;
    input.headSha = pr.head ? pr.head.sha ?? null : null;
    input.mergeSha = pr.merge_commit_sha ?? null;
    input.actorLogin = pr.user ? pr.user.login ?? null : null;
    input.actorType = pr.user ? pr.user.type ?? null : null;
    input.authorAssociation = pr.author_association ?? null;
    const headRepo = pr.head && pr.head.repo ? pr.head.repo.full_name ?? null : null;
    const baseRepo = pr.base && pr.base.repo ? pr.base.repo.full_name ?? repository : repository;
    input.isFork = !headRepo || !baseRepo || headRepo !== baseRepo;
    input.labels = Array.isArray(pr.labels) ? pr.labels.map((label) => String(label.name ?? '')).filter(Boolean) : [];
    input.ref = pr.base ? pr.base.ref ?? null : null;
  } else if (event && typeof event.after === 'string') {
    input.baseSha = event.before ?? null;
    input.headSha = event.after ?? null;
    input.ref = event.ref ?? null;
    input.actorLogin = event.sender ? event.sender.login ?? null : null;
    input.actorType = event.sender ? event.sender.type ?? null : null;
    input.authorAssociation = ownerLogin && event.sender && event.sender.login === ownerLogin ? 'OWNER' : 'NONE';
  }
  return input;
}

/** Parse a changed-file list: one path per line, or TSV `status<TAB>path<TAB>previous_path`. */
export function parseChangedFiles(text) {
  const paths = [];
  for (const rawLine of String(text).split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;
    const parts = line.split('\t');
    if (parts.length >= 2) {
      if (parts[1]) paths.push(parts[1]);
      if (parts[2]) paths.push(parts[2]);
    } else {
      paths.push(parts[0]);
    }
  }
  return paths;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  let policy = null;
  let digest = null;
  let input = { eventName: args.eventName, changedFiles: [], changedFilesAvailable: false };
  let plan;
  try {
    const policyText = readFileSync(args.policy, 'utf8');
    digest = policyDigest(policyText);
    policy = JSON.parse(policyText);
    const event = args.event ? JSON.parse(readFileSync(args.event, 'utf8')) : null;
    let changedFiles = [];
    let changedFilesAvailable = false;
    if (args.changedFiles && existsSync(args.changedFiles)) {
      changedFiles = parseChangedFiles(readFileSync(args.changedFiles, 'utf8'));
      changedFilesAvailable = true;
    }
    input = inputFromEvent(event, args.eventName ?? (event ? null : 'local'), { changedFiles, changedFilesAvailable, executionMode: args.executionMode, mergeTreeSha: args.mergeTreeSha });
    if (!event) {
      // Local what-if: an explicit actor is the operator; default to a trusted owner preview.
      input.actorLogin = 'local';
      input.actorType = 'User';
      input.authorAssociation = 'OWNER';
    }
    for (const [key, value] of Object.entries(args.overrides)) input[key] = value;
    plan = buildPlan(input, policy, digest);
  } catch (error) {
    plan = errorPlan(input, policy, digest, error);
    console.error(`planner error (escalating to the full hosted plan): ${error && error.stack ? error.stack : error}`);
  }
  mkdirSync(dirname(args.out), { recursive: true });
  writeFileSync(args.out, `${JSON.stringify(plan, null, 2)}\n`);
  const summary = renderPlanSummary(plan);
  if (args.summary) appendFileSync(args.summary, summary);
  process.stdout.write(summary);
}

if (process.argv[1] && /plan\.mjs$/.test(process.argv[1])) main();
