import { execFileSync } from 'node:child_process';
import { TextDecoder } from 'node:util';
import { validateContracts } from '../core/contracts.mjs';
import { changedPaths, snapshot } from '../core/snapshot.mjs';
import { planContinuation } from '../core/planner.mjs';
import { glob, hash, invariant, isGitId, matches, validPath } from '../core/primitives.mjs';

export const CONTROL_FLOOR = ['.github/**', '.ci/**', 'ci/**', 'scripts/ci/**'];
const uniqueStrings = value => Array.isArray(value) && value.every(x => typeof x === 'string' && x.length > 0) && new Set(value).size === value.length;

/** A small declarative adapter protocol, not an executable plugin loaded from the PR. */
export function validateManifest(manifest) {
  invariant(manifest?.format === 'ci.repository-adapter.v1', 'unsupported repository adapter');
  invariant(typeof manifest.repositoryId === 'string' && /^[1-9]\d*$/.test(manifest.repositoryId), 'numeric immutable repository ID required');
  invariant(typeof manifest.description === 'string', 'adapter description required');
  invariant(Object.keys(manifest).every(k => ['format', 'repositoryId', 'description', 'policy', 'contracts'].includes(k)), 'unknown adapter field');
  validateContracts(manifest.contracts);
  const tasks = Object.keys(manifest.contracts.tasks), policy = manifest.policy;
  invariant(policy && Object.keys(policy).every(k => ['alwaysTasks', 'controlPaths', 'rules'].includes(k)), 'invalid adapter policy');
  invariant(uniqueStrings(policy.alwaysTasks) && policy.alwaysTasks.every(id => tasks.includes(id)), 'invalid always tasks');
  invariant(uniqueStrings(policy.controlPaths) && CONTROL_FLOOR.every(p => policy.controlPaths.includes(p)), 'protected control floor cannot be removed');
  policy.controlPaths.forEach(glob);
  invariant(Array.isArray(policy.rules) && policy.rules.length > 0, 'explicit ownership rules required');
  for (const rule of policy.rules) {
    invariant(rule && Object.keys(rule).every(k => ['patterns', 'tasks'].includes(k)), 'invalid ownership rule');
    invariant(uniqueStrings(rule.patterns) && rule.patterns.length > 0, 'invalid rule patterns');
    rule.patterns.forEach(glob);
    invariant(uniqueStrings(rule.tasks) && rule.tasks.every(id => tasks.includes(id)), 'unknown or duplicate rule task');
  }
  return manifest;
}

/** Read exact blob bytes from a complete Git tree. No checkout, hooks, project commands or imports. */
export function readJsonBlob(repo, commit, path, maxBytes = 1024 * 1024) {
  invariant(isGitId(commit) && validPath(path), 'exact commit and safe repository-relative path required');
  invariant(Number.isSafeInteger(maxBytes) && maxBytes > 0 && maxBytes <= 8 * 1024 * 1024, 'invalid blob budget');
  const state = snapshot(repo, commit), entry = state.entries.find(e => e.path === path);
  invariant(entry && ['100644', '100755'].includes(entry.mode), 'configuration must be a regular tracked blob');
  const bytes = execFileSync('git', ['--no-replace-objects', '-C', repo, 'cat-file', 'blob', entry.oid],
    { timeout: 30000, maxBuffer: maxBytes, env: { ...process.env, GIT_NO_REPLACE_OBJECTS: '1' } });
  const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  return { value: JSON.parse(text), text, oid: entry.oid, commit, path };
}

/** Always observation. An existing protected selector may supply additional mandatory tasks. */
export function adviseRepository({ manifest, baseState, candidateState, canonicalSelected = [], environments = {}, context = {}, event = 'pull_request', forceFull = false }) {
  validateManifest(manifest);
  const universe = Object.keys(manifest.contracts.tasks).sort(), paths = changedPaths(baseState, candidateState);
  invariant(uniqueStrings(canonicalSelected) && canonicalSelected.every(id => universe.includes(id)), 'invalid canonical floor');
  const selected = new Set([...manifest.policy.alwaysTasks, ...canonicalSelected]);
  const reasons = [];
  if (forceFull) reasons.push('explicit-full');
  if (event !== 'pull_request') reasons.push('non-pr-event');
  for (const path of paths) {
    if (matches(path, manifest.policy.controlPaths)) reasons.push('control-path');
    const rules = manifest.policy.rules.filter(rule => matches(path, rule.patterns));
    if (!rules.length && !matches(path, manifest.policy.controlPaths)) reasons.push('unmapped-path');
    for (const rule of rules) for (const id of rule.tasks) selected.add(id);
  }
  const fullQualification = reasons.length > 0;
  const plan = planContinuation({ universe, graph: manifest.contracts,
    canonicalSelected: fullQualification ? universe : [...selected].sort(), baseState, candidateState,
    mode: 'observe', qualifiedTasks: [], environments, context, repositoryId: manifest.repositoryId,
    policyDigest: hash({ domain: 'ci.repository-adapter.v1', manifest }), fullQualification });
  return { format: 'ci.repository-advisory.v1', authority: 'none', configPolicyDigest: hash(manifest),
    escalationReasons: [...new Set(reasons)].sort(), changedPathCount: paths.length, plan };
}

/** The policy is always read from the caller's trusted base, NEVER the candidate or worktree. */
export function inspectRepository({ repo, base, candidate, configPath = '.ci/continuation.json', repositoryId }) {
  invariant(validPath(configPath) && matches(configPath, CONTROL_FLOOR), 'config must live under a protected control path');
  const config = readJsonBlob(repo, base, configPath);
  invariant(config.value.repositoryId === repositoryId, 'repository binding mismatch');
  const report = adviseRepository({ manifest: config.value, baseState: snapshot(repo, base), candidateState: snapshot(repo, candidate) });
  return { ...report, configSource: { commit: base, path: configPath, blob: config.oid } };
}

/** Conservative starters: commands are identities for review, not executed by this kit. */
export function starterManifest({ repositoryId, kind, root = '.' }) {
  invariant(['node', 'dotnet', 'python'].includes(kind), 'kind must be node, dotnet or python');
  invariant(root === '.' || validPath(root) && !/[*?\[\]{}!]/.test(root), 'invalid project root');
  const prefix = root === '.' ? '' : `${root}/`;
  const specs = {
    node: { command: ['npm', 'test', '--', '--run'], required: [`${prefix}package.json`], runtime: 'node' },
    dotnet: { command: ['dotnet', 'test', '--configuration', 'Release'], required: [`${prefix}**/*.csproj`], runtime: 'dotnet' },
    python: { command: ['python', '-m', 'pytest'], required: [`${prefix}pyproject.toml`], runtime: 'python' }
  };
  const spec = specs[kind];
  return validateManifest({ format: 'ci.repository-adapter.v1', repositoryId,
    description: `Review this ${kind} starter against actual workflows, working directory, fixtures and dependencies before narrowing it.`,
    policy: { alwaysTasks: [], controlPaths: [...CONTROL_FLOOR], rules: [{ patterns: [`${prefix}**`], tasks: ['tests'] }, { patterns: ['docs/**', '**/*.md'], tasks: [] }] },
    contracts: { version: 1, globalInputs: [...CONTROL_FLOOR, '**/package.json', '**/package-lock.json', '**/pnpm-lock.yaml', '**/yarn.lock', '**/global.json', '**/Directory.Build.*', '**/Directory.Packages.props', '**/NuGet.Config', '**/nuget.config', '**/pyproject.toml', '**/requirements*.txt', '**/uv.lock', '**/poetry.lock', '.gitattributes', '.gitmodules'],
      components: { product: { inputs: [`${prefix}**`], deps: [] } },
      tasks: { tests: { components: ['product'], inputs: [], requiredInputs: spec.required,
        command: ['working-directory', root, ...spec.command], platform: 'linux-x64',
        environmentKeys: ['os', 'arch', 'image', spec.runtime, 'dependencies'], contextKeys: ['configuration'],
        reuse: 'eligible', reviewed: false, ttlSeconds: 86400 } } } });
}
