#!/usr/bin/env node

import { execFile } from 'node:child_process';
import {
  mkdirSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { dirname } from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const SHA_PATTERN = /^[0-9a-f]{40}$/i;
export const AUTH_HEADER_ENV = 'TASKDECK_GIT_HTTP_EXTRAHEADER';

export const MAX_ATTEMPTS = 3;
export const RETRY_DELAY_MS = 1_000;

function requireSha(value, label) {
  if (!SHA_PATTERN.test(String(value ?? ''))) {
    throw new Error(`${label} must be a full 40-character Git SHA`);
  }
  return String(value).toLowerCase();
}

function requireOutputPath(value, label) {
  if (typeof value !== 'string' || value.length === 0) {
    throw new Error(`${label} is required`);
  }
  return value;
}

async function executeGitDefault(args, options) {
  const { stdout } = await execFileAsync('git', args, {
    cwd: options.cwd,
    encoding: 'utf8',
    env: options.env,
    maxBuffer: 1024 * 1024,
    windowsHide: true,
  });
  return stdout;
}

function authenticatedGitEnvironment(token) {
  if (typeof token !== 'string' || token.length === 0) {
    throw new Error('GH_TOKEN is required for the read-only merge-ref fetch');
  }
  const basicToken = Buffer.from(`x-access-token:${token}`, 'utf8').toString('base64');
  return {
    ...process.env,
    [AUTH_HEADER_ENV]: `AUTHORIZATION: basic ${basicToken}`,
  };
}

/**
 * Fetch and read one PR merge-ref observation. The token is supplied through Git's
 * config environment support and never appears in the child-process argument list.
 */
export async function observeMergeRef({
  pullRequestNumber,
  token = process.env.GH_TOKEN,
  cwd = process.cwd(),
  executeGit = executeGitDefault,
}) {
  if (!Number.isSafeInteger(pullRequestNumber) || pullRequestNumber <= 0) {
    throw new Error('pull request number must be a positive integer');
  }

  await executeGit([
    `--config-env=http.extraHeader=${AUTH_HEADER_ENV}`,
    'fetch',
    '--no-tags',
    '--depth=2',
    'origin',
    `refs/pull/${pullRequestNumber}/merge`,
  ], {
    cwd,
    env: authenticatedGitEnvironment(token),
  });

  const output = await executeGit([
    'rev-parse',
    'FETCH_HEAD^{commit}',
    'FETCH_HEAD^1',
    'FETCH_HEAD^2',
    'FETCH_HEAD^{tree}',
  ], {
    cwd,
    env: process.env,
  });
  const values = String(output).trim().split(/\r?\n/);
  if (values.length !== 4 || values.some((value) => !SHA_PATTERN.test(value))) {
    throw new Error('the fetched merge ref did not yield one complete four-SHA observation');
  }

  const [mergeSha, mergeBaseSha, headSha, treeSha] = values.map((value) => value.toLowerCase());
  return { mergeSha, mergeBaseSha, headSha, treeSha };
}

/**
 * Read the base branch's current tip on origin. GitHub regenerates `refs/pull/N/merge`
 * against whatever the base branch points at *now*, so when the base advances between the
 * event dispatch and this fetch, the merge ref's first parent is that newer tip rather than
 * the control base. Confirming the observed first parent IS that live tip keeps the
 * binding honest without failing the run (CI-03 #2327).
 */
export async function observeBaseTip({
  baseRef,
  token = process.env.GH_TOKEN,
  cwd = process.cwd(),
  executeGit = executeGitDefault,
}) {
  if (typeof baseRef !== 'string' || baseRef.length === 0) {
    throw new Error('base ref is required to read the base branch tip');
  }

  await executeGit([
    `--config-env=http.extraHeader=${AUTH_HEADER_ENV}`,
    'fetch',
    '--no-tags',
    '--depth=1',
    'origin',
    `refs/heads/${baseRef}`,
  ], {
    cwd,
    env: authenticatedGitEnvironment(token),
  });

  const output = await executeGit(['rev-parse', 'FETCH_HEAD^{commit}'], { cwd, env: process.env });
  const tip = String(output).trim();
  if (!SHA_PATTERN.test(tip)) throw new Error('the fetched base ref did not yield one commit SHA');
  return tip.toLowerCase();
}

function removeOutputs(paths) {
  for (const path of paths) rmSync(path, { force: true });
}

function publishOutputs(observation, outputs) {
  const entries = [
    [outputs.merge, observation.mergeSha],
    [outputs.tree, observation.treeSha],
    [outputs.mergeBase, observation.mergeBaseSha],
    [outputs.mergeBaseTip, observation.mergeBaseTipSha ?? 'null'],
  ];
  const outputPaths = entries.map(([path]) => path);
  const temporaryPaths = outputPaths.map((path) => `${path}.tmp-${process.pid}`);

  try {
    for (const path of outputPaths) mkdirSync(dirname(path), { recursive: true });
    removeOutputs(temporaryPaths);
    entries.forEach(([, value], index) => {
      writeFileSync(temporaryPaths[index], `${value}\n`, { encoding: 'utf8', flag: 'wx' });
    });
    entries.forEach(([path], index) => renameSync(temporaryPaths[index], path));
  } catch (error) {
    removeOutputs([...temporaryPaths, ...outputPaths]);
    throw error;
  }
}

function mismatchReason(observation, expectedBase, expectedHead) {
  if (!observation || typeof observation !== 'object') return 'merge ref unavailable';
  const values = [
    observation.mergeSha,
    observation.mergeBaseSha,
    observation.headSha,
    observation.treeSha,
  ];
  if (values.some((value) => !SHA_PATTERN.test(String(value ?? '')))) return 'invalid observation';
  if (observation.mergeBaseSha.toLowerCase() !== expectedBase
    && observation.headSha.toLowerCase() !== expectedHead) return 'base and head mismatch';
  if (observation.mergeBaseSha.toLowerCase() !== expectedBase) return 'base mismatch';
  if (observation.headSha.toLowerCase() !== expectedHead) return 'head mismatch';
  return null;
}

/**
 * Resolve one exact control-base/event-head merge identity. Failed or mismatched
 * observations are retried twice, then remain fail-closed with no output files.
 *
 * The event head must match exactly — that is the untrusted side of the merge and is never
 * negotiable. The first parent is allowed to be the base branch's *live* tip on origin when
 * the base advanced after dispatch (`merge-ref-moved`): GitHub regenerates the merge ref
 * against the current base, so demanding the dispatch-time control base turned every base
 * push during a run into a planner error (CI-03 #2327). Both accepted first parents are heads
 * of the same branch that already supplies the control-plane tooling, so no untrusted content
 * enters the binding.
 */
export async function resolveMergeRef({
  expectedBase,
  expectedHead,
  mergeOutput,
  treeOutput,
  mergeBaseOutput,
  mergeBaseTipOutput,
  observe,
  resolveBaseTip = null,
  sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  log = () => {},
}) {
  const normalizedBase = requireSha(expectedBase, 'expected base');
  const normalizedHead = requireSha(expectedHead, 'expected head');
  const mergeOutputPath = requireOutputPath(mergeOutput, 'merge output path');
  const treeOutputPath = requireOutputPath(treeOutput, 'tree output path');
  const mergeBaseOutputPath = requireOutputPath(mergeBaseOutput, 'merge base output path');
  const mergeBaseTipOutputPath = requireOutputPath(mergeBaseTipOutput, 'merge base tip output path');
  const outputPaths = [mergeOutputPath, treeOutputPath, mergeBaseOutputPath, mergeBaseTipOutputPath];
  if (new Set(outputPaths).size !== outputPaths.length) throw new Error('merge identity output paths must differ');
  if (typeof observe !== 'function') throw new Error('merge-ref observer is required');

  removeOutputs(outputPaths);
  let finalReason = 'merge ref unavailable';

  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
    let observation = null;
    let movedBase = null;
    try {
      observation = await observe();
      finalReason = mismatchReason(observation, normalizedBase, normalizedHead);
    } catch {
      finalReason = 'merge ref unavailable';
    }

    if (finalReason === 'base mismatch' && typeof resolveBaseTip === 'function') {
      // The head already matched exactly; only the first parent moved. Accept it if — and
      // only if — it is the live tip of the base ref on origin.
      try {
        const tip = await resolveBaseTip();
        if (SHA_PATTERN.test(String(tip ?? '')) && String(tip).toLowerCase() === observation.mergeBaseSha.toLowerCase()) {
          movedBase = String(tip).toLowerCase();
          finalReason = null;
        } else {
          finalReason = 'base mismatch (not the live base branch tip)';
        }
      } catch {
        finalReason = 'base mismatch (the base branch tip could not be read)';
      }
    }

    if (finalReason === null) {
      const resolved = { ...observation, mergeRefMoved: movedBase !== null, mergeBaseTipSha: movedBase };
      publishOutputs(resolved, {
        merge: mergeOutputPath,
        tree: treeOutputPath,
        mergeBase: mergeBaseOutputPath,
        mergeBaseTip: mergeBaseTipOutputPath,
      });
      if (movedBase) {
        log(`merge-ref-moved — the base advanced from ${normalizedBase} to the live base branch tip ${movedBase} after dispatch; the event head matched exactly on attempt ${attempt}/${MAX_ATTEMPTS}`);
      } else {
        log(`merge ref matched the control base and event head on attempt ${attempt}/${MAX_ATTEMPTS}`);
      }
      return resolved;
    }

    if (attempt < MAX_ATTEMPTS) {
      log(`merge ref attempt ${attempt}/${MAX_ATTEMPTS} did not match: ${finalReason}; retrying`);
      await sleep(RETRY_DELAY_MS);
    }
  }

  removeOutputs(outputPaths);
  throw new Error(`merge ref resolution failed closed after ${MAX_ATTEMPTS} attempts: ${finalReason}`);
}

/**
 * Write the planner note for an accepted `merge-ref-moved` resolution. Always LF-terminated so
 * the note the planner receives does not depend on the checkout's line endings.
 */
export function writeMergeRefNote(noteOutput, expectedBase, resolved) {
  mkdirSync(dirname(noteOutput), { recursive: true });
  writeFileSync(
    noteOutput,
    `merge-ref-moved: the base advanced from ${String(expectedBase).toLowerCase()} to ${resolved.mergeBaseTipSha} after dispatch; the merge ref was regenerated against the base branch live tip on origin and the event head matched exactly\n`,
    { encoding: 'utf8' },
  );
}

function parseArgs(argv) {
  const args = {
    pullRequestNumber: null,
    expectedBase: null,
    expectedHead: null,
    baseRef: null,
    mergeOutput: null,
    treeOutput: null,
    mergeBaseOutput: null,
    mergeBaseTipOutput: null,
    noteOutput: null,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    const next = () => argv[++index];
    switch (argument) {
      case '--pr': args.pullRequestNumber = Number(next()); break;
      case '--base': args.expectedBase = next(); break;
      case '--head': args.expectedHead = next(); break;
      case '--base-ref': args.baseRef = next(); break;
      case '--merge-out': args.mergeOutput = next(); break;
      case '--tree-out': args.treeOutput = next(); break;
      case '--merge-base-out': args.mergeBaseOutput = next(); break;
      case '--merge-base-tip-out': args.mergeBaseTipOutput = next(); break;
      case '--note-out': args.noteOutput = next(); break;
      default: throw new Error(`unknown argument: ${argument}`);
    }
  }
  return args;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.noteOutput) rmSync(args.noteOutput, { force: true });
  const resolved = await resolveMergeRef({
    expectedBase: args.expectedBase,
    expectedHead: args.expectedHead,
    mergeOutput: args.mergeOutput,
    treeOutput: args.treeOutput,
    mergeBaseOutput: args.mergeBaseOutput,
    mergeBaseTipOutput: args.mergeBaseTipOutput,
    observe: () => observeMergeRef({
      pullRequestNumber: args.pullRequestNumber,
      token: process.env.GH_TOKEN,
    }),
    resolveBaseTip: args.baseRef
      ? () => observeBaseTip({ baseRef: args.baseRef, token: process.env.GH_TOKEN })
      : null,
    log: (message) => process.stderr.write(`${message}\n`),
  });
  if (args.noteOutput && resolved.mergeRefMoved) {
    writeMergeRefNote(args.noteOutput, args.expectedBase, resolved);
  }
}

if (process.argv[1] && /resolve-merge-ref\.mjs$/i.test(process.argv[1])) {
  main().catch((error) => {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    process.exitCode = 1;
  });
}
