#!/usr/bin/env node
// CI-17 discovery prerequisite. This is NOT an Actions expression evaluator,
// general YAML parser, rehearsal guard, or authorization to skip any CI job.
import { readdirSync, readFileSync, realpathSync, lstatSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const MAX_FILES = 256;
const MAX_FILE_BYTES = 2 * 1024 * 1024;
const MAX_TOTAL_BYTES = 16 * 1024 * 1024;
const MAX_JOBS = 4096;
const MAX_PROJECTED_EVENT_BYTES = 8 * 1024 * 1024;
const MAX_PROJECTED_SURFACE_BYTES = 8 * 1024 * 1024;
const PATH = /^\.github\/workflows\/[A-Za-z0-9_.-]+\.ya?ml$/;
const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const indentation = (line) => line.length - line.trimStart().length;
const significant = (line) => line.trim() && !line.trimStart().startsWith('#');
const blockMapping = (field) => field && /^(?:#.*)?$/.test(field.value.trim());

// Read only the block mapping boundaries needed for workflow -> jobs -> job.
// Reject unfamiliar structural syntax rather than silently losing an entire job.
// Deeper values (steps, matrices, expressions) are deliberately NOT interpreted.
function fields(lines, depth, path, diagnostics) {
  const result = new Map();
  let current = null;
  for (const source of lines) {
    if (!significant(source.text)) {
      if (current) current.children.push(source);
      continue;
    }
    const indent = indentation(source.text);
    if (/^\s*\t/.test(source.text) || indent < depth || (indent > depth && !current)) {
      diagnostics.push({ file: path, line: source.line, code: 'unsupported-indentation' });
      continue;
    }
    if (indent > depth) {
      current.children.push(source);
      continue;
    }
    const match = source.text.slice(depth).match(/^([A-Za-z_][A-Za-z0-9_-]*):(?:\s+(.*))?$/);
    if (!match) {
      diagnostics.push({ file: path, line: source.line, code: 'unsupported-mapping' });
      current = null;
      continue;
    }
    current = { key: match[1], value: match[2] ?? '', line: source.line, depth, children: [] };
    if (result.has(current.key)) diagnostics.push({ file: path, line: source.line, code: 'duplicate-key' });
    // Keep the first occurrence; duplicate diagnostics already make discovery incomplete.
    else result.set(current.key, current);
  }
  return result;
}

function controlText(field) {
  if (!field) return null;
  // Preserve blank/hash-prefixed lines inside YAML block scalars. Removing them
  // can erase a meaningful change to a caller input or a dynamic selector.
  const prefix = ' '.repeat(field.depth + 2);
  const children = field.children.map(({ text }) => text.startsWith(prefix) ? text.slice(prefix.length) : text);
  const keepsTrailingLines = /[|>][^\s]*\+/.test(field.value) ||
    children.some((line) => /:\s+.*[|>][^\s]*\+(?:[ \t]+#.*)?\s*$/.test(line));
  if (!keepsTrailingLines) {
    while (children.length && !children.at(-1).trim()) children.pop();
  }
  return [field.value.trim(), ...children].filter((line, index) => index !== 0 || line).join('\n');
}

function literal(text) {
  if (typeof text !== 'string') return null;
  const match = text.match(/^(?:([A-Za-z0-9_./@-]+)|'([A-Za-z0-9_./@-]+)'|"([A-Za-z0-9_./@-]+)")(?:[ \t]+#.*)?$/);
  return match ? (match[1] ?? match[2] ?? match[3]) : null;
}

function classification(selector) {
  const value = literal(selector);
  // This finite list describes reviewed selector spelling, NOT actual runner identity.
  if (['ubuntu-latest', 'ubuntu-22.04', 'ubuntu-24.04'].includes(value)) return 'linux-literal';
  if (value && /^windows-(?:latest|\d[\w.-]*)$/.test(value)) return 'windows-literal';
  return 'opaque';
}

function report(runners, calls, diagnostics) {
  return {
    schemaVersion: 1,
    kind: 'smart-ci-workflow-runner-inventory',
    graphComplete: diagnostics.length === 0,
    runners: runners.sort((a, b) => compare(a.id, b.id)),
    calls: calls.sort((a, b) => compare(a.id, b.id)),
    diagnostics: diagnostics.sort((a, b) => compare(a.file ?? '', b.file ?? '') || (a.line ?? 0) - (b.line ?? 0) || compare(a.code, b.code)),
  };
}

/** Inspect a complete supplied workflow set; never evaluate conditions or runner expressions. */
export function inventoryWorkflowRunners(files) {
  const diagnostics = [];
  const runners = [];
  const calls = [];
  const sources = new Map();
  if (!Array.isArray(files)) return report([], [], [{ code: 'invalid-source' }]);
  if (files.length === 0) return report([], [], [{ code: 'empty-source' }]);
  if (files.length > MAX_FILES) return report([], [], [{ code: 'source-limit' }]);
  let bytes = 0;
  for (const source of files) {
    if (!source || typeof source.path !== 'string' || !PATH.test(source.path) || typeof source.text !== 'string') {
      diagnostics.push({ code: 'invalid-source' });
      continue;
    }
    const size = Buffer.byteLength(source.text, 'utf8');
    bytes += size;
    if (size > MAX_FILE_BYTES || bytes > MAX_TOTAL_BYTES) return report([], [], [{ code: 'source-limit' }]);
    if (sources.has(source.path)) diagnostics.push({ file: source.path, code: 'duplicate-file' });
    else sources.set(source.path, source.text);
  }

  let jobCount = 0;
  let projectedEventBytes = 0;
  for (const [path, text] of [...sources].sort(([a], [b]) => compare(a, b))) {
    const lines = text.split(/\r?\n/).map((line, index) => ({ text: line, line: index + 1 }));
    const top = fields(lines, 0, path, diagnostics);
    const jobsField = top.get('jobs');
    if (!jobsField) { diagnostics.push({ file: path, code: 'jobs-required' }); continue; }
    if (!blockMapping(jobsField)) { diagnostics.push({ file: path, line: jobsField.line, code: 'unsupported-jobs-mapping' }); continue; }
    const jobs = fields(jobsField.children, 2, path, diagnostics);
    if (jobs.size === 0) diagnostics.push({ file: path, code: 'jobs-required' });
    const events = controlText(top.get('on'));
    projectedEventBytes += Buffer.byteLength(events ?? '', 'utf8') * jobs.size;
    if (projectedEventBytes > MAX_PROJECTED_EVENT_BYTES) {
      diagnostics.push({ file: path, line: top.get('on')?.line, code: 'projection-limit' });
      return report([], [], diagnostics);
    }
    for (const [job, node] of jobs) {
      jobCount += 1;
      if (jobCount > MAX_JOBS) return report([], [], [{ code: 'source-limit' }]);
      if (!blockMapping(node)) { diagnostics.push({ file: path, line: node.line, code: 'unsupported-job-mapping' }); continue; }
      const properties = fields(node.children, 4, path, diagnostics);
      const shared = {
        id: `${path}#${job}`, file: path, job, line: node.line,
        events,
        condition: controlText(properties.get('if')),
        needs: controlText(properties.get('needs')),
        strategy: controlText(properties.get('strategy')),
      };
      const selector = properties.get('runs-on');
      const uses = properties.get('uses');
      if (selector && uses) diagnostics.push({ file: path, line: node.line, code: 'ambiguous-job' });
      if (!selector && !uses) diagnostics.push({ file: path, line: node.line, code: 'runner-or-call-required' });
      if (selector) {
        const value = controlText(selector);
        const scalar = selector.children.some((source) => significant(source.text)) ? value : selector.value.trim();
        runners.push({ ...shared, selector: value, classification: classification(scalar) });
      }
      if (uses) {
        const reference = controlText(uses);
        const target = literal(reference);
        const callee = target?.startsWith('./') && PATH.test(target.slice(2)) ? target.slice(2) : null;
        calls.push({ ...shared, reference, callee, inputs: controlText(properties.get('with')) });
        if (!callee) diagnostics.push({ file: path, line: uses.line, code: 'unresolved-workflow' });
        else if (!sources.has(callee)) diagnostics.push({ file: path, line: uses.line, code: 'missing-workflow' });
      }
    }
  }

  const outgoing = new Map([...sources.keys()].map((path) => [path, []]));
  for (const call of calls) if (call.callee && sources.has(call.callee)) outgoing.get(call.file).push(call.callee);
  const visiting = new Set();
  const visited = new Set();
  function visit(path) {
    if (visiting.has(path)) { diagnostics.push({ file: path, code: 'workflow-cycle' }); return; }
    if (visited.has(path)) return;
    visiting.add(path);
    for (const callee of outgoing.get(path)) visit(callee);
    visiting.delete(path);
    visited.add(path);
  }
  for (const path of [...sources.keys()].sort(compare)) visit(path);
  return report(runners, calls, diagnostics);
}

/** Stable review surface: all non-reviewed-Linux selector spellings plus every ancestor call. */
export function reviewedRunnerSurface(inventory) {
  const withoutLine = ({ line: _line, ...entry }) => entry;
  const candidates = [];
  let projectedBytes = 0;
  for (const entry of inventory.runners.filter((item) => item.classification !== 'linux-literal')) {
    const workflows = new Set([entry.file]);
    const callers = new Map();
    const pending = [entry.file];
    while (pending.length) {
      const target = pending.pop();
      for (const call of inventory.calls.filter((edge) => edge.callee === target)) {
        callers.set(call.id, withoutLine(call));
        if (!workflows.has(call.file)) { workflows.add(call.file); pending.push(call.file); }
      }
    }
    const base = withoutLine(entry);
    const orderedCallers = [...callers.values()].sort((a, b) => compare(a.id, b.id));
    const candidate = { ...base, callers: orderedCallers };
    projectedBytes += 2 * Buffer.byteLength(JSON.stringify(candidate), 'utf8') + 256;
    if (projectedBytes > MAX_PROJECTED_SURFACE_BYTES) {
      return { schemaVersion: 1, graphComplete: false, candidates: [] };
    }
    candidates.push(candidate);
  }
  return { schemaVersion: 1, graphComplete: inventory.graphComplete, candidates };
}

/** Load regular workflow files only; reject symlink entries. No remote calls or workflow execution. */
export function loadWorkflowSources(directory) {
  const names = readdirSync(directory).filter((name) => /\.ya?ml$/i.test(name)).sort(compare);
  if (names.length > MAX_FILES) throw new Error('Workflow source limit exceeded');
  let total = 0;
  return names.map((name) => {
    const location = join(directory, name);
    const stat = lstatSync(location);
    total += stat.size;
    if (!stat.isFile() || stat.size > MAX_FILE_BYTES || total > MAX_TOTAL_BYTES) throw new Error('Unsupported workflow source');
    return { path: `.github/workflows/${name}`, text: readFileSync(location, 'utf8') };
  });
}

function main() {
  let result;
  try {
    const args = process.argv.slice(2);
    if (args.length !== 0 && (args.length !== 2 || args[0] !== '--workflows' || !args[1] || args[1].startsWith('--'))) throw new Error('Invalid arguments');
    result = inventoryWorkflowRunners(loadWorkflowSources(args[1] ?? '.github/workflows'));
  } catch {
    result = report([], [], [{ code: 'source-unavailable' }]);
  }
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
  // Exit zero means discovery succeeded, never that rehearsal is safe or authorized.
  if (!result.graphComplete) process.exitCode = 2;
}

function isMain() {
  try { return Boolean(process.argv[1]) && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url)); }
  catch { return false; }
}
if (isMain()) main();
