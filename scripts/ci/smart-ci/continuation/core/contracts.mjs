import { closure, compare, glob, hash, invariant, isDigest, isGitId, matches, unique, validPath } from './primitives.mjs';

/** Input contracts supplement an existing selection policy; they are NOT a risk classifier. */
export function validateContracts(graph) {
  invariant(graph?.version === 1, 'unsupported contract version');
  invariant(Array.isArray(graph.globalInputs) && graph.globalInputs.length > 0, 'global inputs required');
  graph.globalInputs.forEach(glob);
  invariant(graph.components && Object.keys(graph.components).length > 0, 'components required');
  invariant(graph.tasks && Object.keys(graph.tasks).length > 0, 'tasks required');
  for (const [id, component] of Object.entries(graph.components)) {
    invariant(Array.isArray(component.inputs) && component.inputs.length > 0, `inputs missing: ${id}`);
    component.inputs.forEach(glob);
    invariant(Array.isArray(component.deps), `deps missing: ${id}`);
    closure(id, graph.components);
  }
  for (const [id, task] of Object.entries(graph.tasks)) {
    invariant(/^[a-z0-9-]+$/.test(id), 'invalid task id');
    invariant(Array.isArray(task.components) && task.components.length > 0, `components missing: ${id}`);
    task.components.forEach(c => closure(c, graph.components));
    invariant(Array.isArray(task.inputs) && Array.isArray(task.requiredInputs), `input arrays missing: ${id}`);
    [...task.inputs, ...task.requiredInputs].forEach(glob);
    invariant(Array.isArray(task.command) && task.command.length > 0 && task.command.every(x => typeof x === 'string' && x.length > 0), `command missing: ${id}`);
    invariant(typeof task.platform === 'string' && task.platform.length > 0, `platform missing: ${id}`);
    invariant(Array.isArray(task.environmentKeys) && ['os', 'arch', 'image'].every(x => task.environmentKeys.includes(x)), `environment identity incomplete: ${id}`);
    invariant(Array.isArray(task.contextKeys), `context keys missing: ${id}`);
    invariant(['never', 'eligible'].includes(task.reuse), `reuse policy missing: ${id}`);
    invariant(typeof task.reviewed === 'boolean', `review state missing: ${id}`);
    invariant(Number.isSafeInteger(task.ttlSeconds) && task.ttlSeconds > 0, `TTL missing: ${id}`);
  }
  return graph;
}
export function inputPatterns(graph, taskId) {
  const task = graph.tasks[taskId];
  invariant(task, `unknown task: ${taskId}`);
  const components = new Set();
  task.components.forEach(c => closure(c, graph.components, new Set(), components));
  return unique([...graph.globalInputs, ...task.inputs, ...[...components].flatMap(c => graph.components[c].inputs)]);
}
/** Conservatively ADD tasks implied by changed inputs. Caller may not use this to subtract canonical lanes. */
export function expandAffected(graph, canonicalSelected, paths) {
  validateContracts(graph);
  canonicalSelected.forEach(id => invariant(Object.hasOwn(graph.tasks, id), `unknown canonical lane: ${id}`));
  if (paths.some(path => !validPath(path))) return { selected: Object.keys(graph.tasks).sort(), fallback: 'invalid-path' };
  const allPatterns = [...graph.globalInputs, ...Object.values(graph.components).flatMap(c => c.inputs), ...Object.values(graph.tasks).flatMap(t => t.inputs)];
  const unknown = paths.filter(path => !matches(path, allPatterns));
  if (unknown.length > 0) return { selected: Object.keys(graph.tasks).sort(), fallback: 'unmapped-path', unknown };
  const result = new Set(canonicalSelected);
  for (const id of Object.keys(graph.tasks)) if (paths.some(path => matches(path, inputPatterns(graph, id)))) result.add(id);
  return { selected: [...result].sort(), fallback: null };
}

/** Fingerprints immutable Git input sets, commands, graph, policy, runtime and declared event inputs. */
export function fingerprint({ graph, taskId, state, environment, context, policyDigest, repositoryId }) {
  validateContracts(graph);
  const task = graph.tasks[taskId];
  invariant(task, `unknown task: ${taskId}`);
  invariant(isDigest(policyDigest), 'invalid canonical policy digest');
  invariant(typeof repositoryId === 'string' && /^\d+$/.test(repositoryId), 'numeric repository ID required');
  invariant(state?.complete === true && isGitId(state.commit) && isGitId(state.tree), 'complete immutable snapshot required');
  const reasons = [];
  const seen = new Set();
  for (const entry of state.entries) {
    invariant(validPath(entry.path) && isGitId(entry.oid), 'invalid snapshot entry');
    invariant(!seen.has(entry.path), 'duplicate snapshot entry'); seen.add(entry.path);
    invariant(['100644', '100755', '120000', '160000'].includes(entry.mode), 'unsupported snapshot mode');
    invariant(entry.type === (entry.mode === '160000' ? 'commit' : 'blob'), 'snapshot mode/type mismatch');
    // Conservative for cross-tree links/submodules: their closure may escape the declared inputs.
    if (entry.mode === '120000' || entry.mode === '160000') reasons.push('symlink-or-submodule-needs-explicit-model');
  }
  const patterns = inputPatterns(graph, taskId);
  const entries = state.entries.filter(entry => matches(entry.path, patterns))
    .map(({ path, mode, type, oid }) => ({ path, mode, type, oid })).sort((a, b) => compare(a.path, b.path));
  if (entries.length === 0) reasons.push('empty-input-set');
  for (const required of task.requiredInputs) if (!entries.some(entry => matches(entry.path, [required]))) reasons.push(`missing-input:${required}`);
  const env = {};
  for (const key of task.environmentKeys) {
    if (typeof environment?.[key] !== 'string' || environment[key].trim() === '' || /^(unknown|latest|.+-latest|.+\.x)$/i.test(environment[key])) reasons.push(`unresolved-environment:${key}`);
    else env[key] = environment[key];
  }
  const ctx = {};
  for (const key of task.contextKeys) {
    if (!Object.hasOwn(context ?? {}, key) || context[key] === null || context[key] === undefined) reasons.push(`missing-context:${key}`);
    else ctx[key] = context[key];
  }
  if (!task.reviewed) reasons.push('input-contract-not-reviewed');
  if (task.reuse === 'never') reasons.push('task-always-fresh');
  const graphDigest = hash(graph);
  const key = hash({ domain: 'ci.task-input.v1', repositoryId, taskId, graphDigest, policyDigest,
    platform: task.platform, command: task.command, inputs: entries, environment: env, context: ctx });
  return { taskId, key, graphDigest, policyDigest, repositoryId, platform: task.platform, inputCount: entries.length,
    reusable: reasons.length === 0, reasons: unique(reasons), ttlSeconds: task.ttlSeconds };
}
