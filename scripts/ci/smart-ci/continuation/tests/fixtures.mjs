import { hash } from '../core/primitives.mjs';
export const oid = char => char.repeat(40);
export const digest = char => `sha256:${char.repeat(64)}`;
export const entry = (path, char = 'a', mode = '100644') => ({ path, oid: oid(char), mode, type: mode === '160000' ? 'commit' : 'blob' });
export function graph() {
  const task = components => ({ components, inputs: [], requiredInputs: [], command: ['test', '--all'], platform: 'linux-x64',
    environmentKeys: ['os', 'arch', 'image', 'node', 'dependencies'], contextKeys: ['config'], reuse: 'eligible', reviewed: true, ttlSeconds: 3600 });
  return { version: 1, globalInputs: ['ci/**', 'lock.json'], components: {
    shared: { inputs: ['shared/**'], deps: [] }, backend: { inputs: ['backend/**'], deps: ['shared'] },
    frontend: { inputs: ['frontend/**'], deps: ['shared'] }, docs: { inputs: ['docs/**'], deps: [] }
  }, tasks: { backend: task(['backend']), frontend: task(['frontend']), e2e: task(['backend', 'frontend']), docs: task(['docs']) } };
}
export const state = () => ({ commit: oid('1'), tree: oid('2'), complete: true,
  entries: [entry('backend/main.cs'), entry('frontend/main.ts'), entry('shared/types.json'), entry('ci/controls.json'), entry('lock.json'), entry('docs/readme.md')] });
export const environment = () => ({ os: 'ubuntu-24.04', arch: 'x64', image: '20260901.1', node: '24.13.1', dependencies: 'resolved-sha256-1234' });
export const request = () => ({ graph: graph(), taskId: 'backend', state: state(), environment: environment(), context: { config: 'release' }, policyDigest: digest('f'), repositoryId: '1234' });
export const record = fp => ({ version: 1, issuer: 'protected-collector', keyId: 'test-key', repositoryId: fp.repositoryId,
  taskId: fp.taskId, inputKey: fp.key, policyDigest: fp.policyDigest, graphDigest: fp.graphDigest, platform: fp.platform,
  commit: oid('1'), tree: oid('2'), workflowId: '1', runId: '2', jobId: '3', attempt: 1,
  conclusion: 'success', executedTests: 10, completedAt: 1000, expiresAt: 4600 });
export const producerExpected = () => ({ repositoryId: '1234', workflowId: '1', workflowPath: '.github/workflows/test.yml',
  workflowRevision: oid('1'), commit: oid('1'), tree: oid('2'), taskId: 'backend', inputKey: digest('a'),
  policyDigest: digest('b'), graphDigest: digest('c'), platform: 'linux-x64', commandDigest: hash(['test']),
  environmentDigest: hash(environment()), requiresTests: true });
export const producerObserved = expected => ({ ...expected, workflowDefinitionTrusted: true, inputManifestRecomputed: true,
  status: 'completed', conclusion: 'success', attempt: 1, earlierFailure: false, allowFailure: false,
  coverageComplete: true, executedChecks: 1, executedTests: 10 });
