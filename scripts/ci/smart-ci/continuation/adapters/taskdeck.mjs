#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { validateContracts, fingerprint, expandAffected } from '../core/contracts.mjs';
import { invariant } from '../core/primitives.mjs';
import { snapshot, changedPaths } from '../core/snapshot.mjs';
import { readJsonBlob } from './repository.mjs';

export const AUDITED_MAIN = '6c51b09bcdcefbc7a852a7047aa9ab8ee5eb10b7';
// Match the canonical existing implementation exactly (raw text, CRLF normalized).
export const canonicalPolicyDigest = text => `sha256:${createHash('sha256').update(text.replace(/\r\n/g, '\n')).digest('hex')}`;

/** Coarse initial input contracts. No supplied Taskdeck lane is approved for result reuse. */
export function taskdeckContracts(policy) {
  invariant(policy?.schemaVersion === 1 && policy.policyId === 'taskdeck.smart-ci.v1', 'canonical Taskdeck v1 policy required');
  invariant(policy.lanes && Object.keys(policy.lanes).length > 0, 'canonical lane inventory required');
  const graph = {
    version: 1,
    globalInputs: ['.github/**', 'ci/**', 'scripts/ci/**', '.config/**', 'global.json', '**/global.json',
      '**/Directory.Build.*', '**/Directory.Packages.props', '**/NuGet.Config', '**/nuget.config',
      '**/package.json', '**/package-lock.json', '**/.npmrc', '**/.nvmrc', '.gitattributes', '.gitmodules',
      '.dockerignore', '**/.dockerignore', '**/Dockerfile*', 'package.json', 'package-lock.json'],
    components: {
      repository: { inputs: ['**'], deps: [] },
      backend: { inputs: ['backend/**'], deps: [] },
      frontend: { inputs: ['frontend/**'], deps: [] },
      scripts: { inputs: ['scripts/**'], deps: [] },
      launcher: { inputs: ['scripts/**'], deps: ['backend', 'frontend'] },
      journeys: { inputs: ['tests/**', 'deploy/**'], deps: ['backend', 'frontend', 'scripts'] }
    }, tasks: {}
  };
  for (const [id, lane] of Object.entries(policy.lanes)) {
    let components = ['repository'];
    if (id.startsWith('backend-') || id.startsWith('api-integration') || id === 'migration-validation') components = ['backend', 'scripts'];
    if (id === 'frontend-unit-windows' || id === 'paper-color-audit') components = ['frontend', 'scripts'];
    // Preserve broad historical contracts when inspecting pre-split canonical plans.
    if (id === 'frontend-unit-linux') components = Object.hasOwn(policy.lanes, 'source-launcher-linux') ? ['frontend', 'scripts'] : ['launcher'];
    if (id === 'source-launcher-linux') components = ['launcher'];
    if (id === 'e2e-smoke' || id === 'container-images') components = ['journeys'];
    const isWindows = id.endsWith('-windows') || id === 'worktree-helper-windows';
    graph.tasks[id] = {
      components, inputs: [], requiredInputs: components.includes('backend') ? ['backend/Taskdeck.sln'] : [],
      // Opaque canonical lane identity: NOT a shell command, and not executable by this adapter.
      // Workflow file bytes are in globalInputs. Production integration must bind resolved inputs, callee and job IDs.
      command: ['taskdeck-canonical-lane', id, lane.checkName],
      platform: isWindows ? 'windows-x64' : 'linux-x64',
      environmentKeys: ['os', 'arch', 'image', 'node', 'dotnet', 'shell', 'dependencyResolution', 'networkPolicy'],
      contextKeys: ['testConfiguration', 'environmentContractVersion'],
      reuse: ['security', 'control'].includes(lane.family) || id === 'smart-ci-self-test' ? 'never' : 'eligible',
      reviewed: false,
      ttlSeconds: 24 * 60 * 60
    };
  }
  return validateContracts(graph);
}

/** Read-only auxiliary report. It can ADD affected lanes, but never writes executable job outputs. */
export function adviseTaskdeck({ policyText, plan, baseState, candidateState, environments = {}, context = {} }) {
  const policy = JSON.parse(policyText), digest = canonicalPolicyDigest(policyText), graph = taskdeckContracts(policy);
  const universe = Object.keys(policy.lanes).sort();
  invariant(plan.policyDigest === digest && plan.policyId === policy.policyId && plan.schemaVersion === 1, 'canonical plan/policy binding mismatch');
  const selected = plan.selected.map(x => x.lane), skipped = plan.skipped.map(x => x.lane);
  const all = [...selected, ...skipped];
  invariant(all.length === universe.length && new Set(all).size === all.length && all.every(x => universe.includes(x)), 'canonical lane inventory mismatch');
  invariant(plan.mergeSha === candidateState.commit && plan.mergeTreeSha === candidateState.tree, 'candidate must be the planned merge commit/tree');
  invariant(baseState.commit === (plan.mergeBaseSha ?? plan.baseSha), 'comparison must use actual planned merge first parent');
  const paths = changedPaths(baseState, candidateState);
  const expanded = expandAffected(graph, selected, paths);
  const full = plan.escalated || plan.plannerError || plan.risk === 'R4' || plan.trust !== 'T1';
  const suggested = full ? universe : expanded.selected;
  const report = {
    format: 'taskdeck-continuation-advisory.v1', authority: 'none: canonical ci-plan.v1 and ci-run.v1 remain authoritative',
    auditedMain: AUDITED_MAIN, evaluatedCommit: candidateState.commit, evaluatedTree: candidateState.tree,
    policyDigest: digest, canonicalMode: policy.mode, canonicalSelected: selected,
    advisorySelected: suggested, addedOnly: suggested.filter(id => !selected.includes(id)),
    fallback: full ? 'canonical-conservative-plan' : expanded.fallback,
    changedPathCount: paths.length, candidates: []
  };
  for (const taskId of suggested) report.candidates.push(fingerprint({ graph, taskId, state: candidateState,
    environment: environments[taskId] ?? {}, context, policyDigest: digest, repositoryId: '1098648347' }));
  invariant(report.candidates.every(x => !x.reusable), 'shipped adapter must never authorize reuse');
  return report;
}

async function main() {
  const args = process.argv.slice(2), options = {};
  for (let i = 0; i < args.length; i += 2) {
    invariant(['--repo', '--plan', '--out'].includes(args[i]) && args[i + 1], 'usage: node adapters/taskdeck.mjs --repo REPO --plan ci-plan.json --out NEW_REPORT.json');
    options[args[i].slice(2)] = args[i + 1];
  }
  invariant(options.repo && options.plan && options.out, '--repo, --plan and --out required');
  const repo = resolve(options.repo), out = resolve(options.out);
  invariant(!existsSync(out), 'output already exists');
  const plan = JSON.parse(readFileSync(options.plan, 'utf8'));
  const { text: policyText, value: policy } = readJsonBlob(repo, plan.baseSha, 'ci/policy.v1.json');
  // Run ONLY from a reviewed/protected tooling checkout. The CLI does not execute PR code.
  // Validation uses the existing canonical module instead of cloning its full schema/logic.
  const canonical = await import(new URL('../../lib/plan.mjs', import.meta.url).href);
  const errors = [...canonical.validatePolicy(policy), ...canonical.validatePlan(plan, policy)];
  invariant(errors.length === 0, `canonical validation failed: ${errors.join('; ')}`);
  const report = adviseTaskdeck({ policyText, plan,
    baseState: snapshot(repo, plan.mergeBaseSha ?? plan.baseSha), candidateState: snapshot(repo, plan.mergeSha) });
  writeFileSync(out, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  console.log(`Read-only report written to ${out}. Reuse is disabled for all supplied Taskdeck contracts.`);
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
