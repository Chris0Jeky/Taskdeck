#!/usr/bin/env node
// Smart CI / Required Gate evaluator (ADR-0066, CI-03 #2327).
//
//   node scripts/ci/smart-ci/evaluate-gate.mjs --plan artifacts/ci-plan.json --policy ci/policy.v1.json \
//     --mode shadow|enforce --expected-head <sha> --expected-base <sha> --plan-job-result success \
//     [--results results.json] [--receipt artifacts/ci-run.json] [--summary "$GITHUB_STEP_SUMMARY"]
//
// Exit 1 when the verdict is not ok. In shadow mode only planner/plan defects are red
// (missing or invalid plan, planner error, plan job failure, SHA or policy-digest mismatch);
// missing job evidence is reported as "would fail" and left green.

import { existsSync, mkdirSync, readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { evaluateGate, policyDigest, renderGateSummary } from './lib/plan.mjs';
import { inputFromEvent } from './plan.mjs';

function parseArgs(argv) {
  const args = { plan: 'artifacts/ci-plan.json', policy: null, mode: null, event: null, eventName: process.env.GITHUB_EVENT_NAME ?? null, expectedHead: null, expectedBase: null, planJobResult: null, results: null, receipt: null, summary: null };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--plan': args.plan = next(); break;
      case '--policy': args.policy = next(); break;
      case '--mode': args.mode = next(); break;
      case '--event': args.event = next(); break;
      case '--event-name': args.eventName = next(); break;
      case '--expected-head': args.expectedHead = next(); break;
      case '--expected-base': args.expectedBase = next(); break;
      case '--plan-job-result': args.planJobResult = next(); break;
      case '--results': args.results = next(); break;
      case '--receipt': args.receipt = next(); break;
      case '--summary': args.summary = next(); break;
      case '--help':
        console.log('usage: evaluate-gate.mjs --plan <file> [--policy <file>] --mode shadow|enforce [--expected-head S] [--expected-base S] [--plan-job-result R] [--results <json>] [--receipt <file>] [--summary <file>]');
        process.exit(0);
        break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  if (args.mode !== null && !['shadow', 'enforce'].includes(args.mode)) throw new Error('--mode must be shadow or enforce (omit it to follow the plan/policy mode)');
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  let plan = null;
  if (existsSync(args.plan)) {
    try {
      plan = JSON.parse(readFileSync(args.plan, 'utf8'));
    } catch (error) {
      console.error(`plan unreadable: ${error}`);
      plan = null;
    }
  }
  const policyText = args.policy && existsSync(args.policy) ? readFileSync(args.policy, 'utf8') : null;
  const expectedPolicyDigest = policyText ? policyDigest(policyText) : null;
  let policy = null;
  if (policyText) {
    try {
      policy = JSON.parse(policyText);
    } catch (error) {
      console.error(`policy unreadable: ${error}`);
    }
  }
  const results = args.results && existsSync(args.results) ? JSON.parse(readFileSync(args.results, 'utf8')) : null;
  let eventInput = null;
  if (args.event && existsSync(args.event)) {
    try {
      eventInput = inputFromEvent(JSON.parse(readFileSync(args.event, 'utf8')), args.eventName, {});
    } catch (error) {
      console.error(`event payload unreadable: ${error}`);
    }
  }
  const verdict = evaluateGate(plan, {
    mode: args.mode ?? (policy && policy.mode) ?? null,
    eventInput,
    expectedHeadSha: args.expectedHead || null,
    expectedBaseSha: args.expectedBase || null,
    expectedPolicyDigest,
    policy,
    planJobResult: args.planJobResult || null,
    results,
  });
  const summary = renderGateSummary(verdict, plan);
  if (args.summary) appendFileSync(args.summary, summary);
  process.stdout.write(summary);
  if (args.receipt) {
    mkdirSync(dirname(args.receipt), { recursive: true });
    const receipt = {
      schemaVersion: 1,
      kind: 'smart-ci-gate-receipt',
      mode: verdict.mode,
      ok: verdict.ok,
      wouldFail: verdict.wouldFail,
      failures: verdict.failures,
      notes: verdict.notes,
      policyId: plan ? plan.policyId : null,
      policyDigest: plan ? plan.policyDigest : null,
      event: plan ? plan.event : null,
      baseSha: plan ? plan.baseSha : null,
      headSha: plan ? plan.headSha : null,
      mergeSha: plan ? plan.mergeSha : null,
      mergeTreeSha: plan ? plan.mergeTreeSha : null,
      risk: plan ? plan.risk : null,
      trust: plan ? plan.trust : null,
      escalated: plan ? plan.escalated : null,
      selected: plan ? plan.selected.map((entry) => ({ lane: entry.lane, checkName: entry.checkName, runnerClass: entry.runnerClass, hosted: entry.hosted })) : [],
      skipped: plan ? plan.skipped.map((entry) => ({ lane: entry.lane, checkName: entry.checkName, reason: entry.reason })) : [],
      generatedAtUtc: new Date().toISOString(),
    };
    writeFileSync(args.receipt, `${JSON.stringify(receipt, null, 2)}\n`);
  }
  process.exit(verdict.ok ? 0 : 1);
}

main();
