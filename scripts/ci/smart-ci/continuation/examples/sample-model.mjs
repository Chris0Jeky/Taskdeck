// Small fictional repository, deliberately audited/eligible for demonstration only.
export const oid = c => c.repeat(40);
export const digest = c => `sha256:${c.repeat(64)}`;
export function sample() {
  const task = components => ({ components, inputs: [], requiredInputs: [], command: ['runner', '--complete-suite'],
    platform: 'linux-x64', environmentKeys: ['os', 'arch', 'image', 'node', 'dependencies'], contextKeys: ['configuration'],
    reuse: 'eligible', reviewed: true, ttlSeconds: 3600 });
  const graph = { version: 1, globalInputs: ['ci/**', 'lock.json'], components: {
    backend: { inputs: ['backend/**'], deps: [] }, frontend: { inputs: ['frontend/**'], deps: [] },
    docs: { inputs: ['docs/**'], deps: [] }
  }, tasks: { backend: task(['backend']), frontend: task(['frontend']), e2e: task(['backend', 'frontend']), docs: task(['docs']) } };
  const entries = ['backend/main.cs', 'frontend/main.ts', 'docs/readme.md', 'ci/policy.json', 'lock.json']
    .map(path => ({ path, mode: '100644', type: 'blob', oid: oid('a') }));
  const baseState = { commit: oid('1'), tree: oid('2'), complete: true, entries };
  const environment = { os: 'ubuntu-24.04', arch: 'x64', image: '20260901.1', node: '24.13.1', dependencies: digest('a') };
  return { graph, universe: Object.keys(graph.tasks), canonicalSelected: ['backend', 'frontend', 'e2e', 'docs'],
    baseState, candidateState: structuredClone(baseState), policyDigest: digest('f'), repositoryId: '1234',
    environments: Object.fromEntries(Object.keys(graph.tasks).map(id => [id, environment])), context: { configuration: 'Release' },
    qualifiedTasks: ['backend', 'frontend', 'e2e', 'docs'] };
}
