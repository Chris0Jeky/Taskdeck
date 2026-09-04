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
const AUTH_HEADER_ENV = 'TASKDECK_GIT_HTTP_EXTRAHEADER';

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

  const [mergeSha, baseSha, headSha, treeSha] = values.map((value) => value.toLowerCase());
  return { mergeSha, baseSha, headSha, treeSha };
}

/**
 * Read the protected base branch's current tip. GitHub regenerates `refs/pull/N/merge`
 * against whatever the base branch points at *now*, so when the base advances between the
 * event dispatch and this fetch, the merge ref's first parent is that newer tip rather than
 * the control base. Confirming the observed first parent IS the live protected tip keeps the
 * binding honest without failing the run (CI-03 #2327).
 */
export async function observeBaseTip({
  baseRef,
  token = process.env.GH_TOKEN,
  cwd = process.cwd(),
  executeGit = executeGitDefault,
}) {
  if (typeof baseRef !== 'string' || baseRef.length === 0) {
    throw new Error('base ref is required to read the protected base tip');
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

function publishOutputs(observation, mergeOutput, treeOutput) {
  const mergeTemporary = `${mergeOutput}.tmp-${process.pid}`;
  const treeTemporary = `${treeOutput}.tmp-${process.pid}`;
  const temporaryPaths = [mergeTemporary, treeTemporary];
  const outputPaths = [mergeOutput, treeOutput];

  try {
    mkdirSync(dirname(mergeOutput), { recursive: true });
    mkdirSync(dirname(treeOutput), { recursive: true });
    removeOutputs(temporaryPaths);
    writeFileSync(mergeTemporary, `${observation.mergeSha}\n`, { encoding: 'utf8', flag: 'wx' });
    writeFileSync(treeTemporary, `${observation.treeSha}\n`, { encoding: 'utf8', flag: 'wx' });
    renameSync(mergeTemporary, mergeOutput);
    renameSync(treeTemporary, treeOutput);
  } catch (error) {
    removeOutputs([...temporaryPaths, ...outputPaths]);
    throw error;
  }
}

function mismatchReason(observation, expectedBase, expectedHead) {
  if (!observation || typeof observation !== 'object') return 'merge ref unavailable';
  const values = [
    observation.mergeSha,
    observation.baseSha,
    observation.headSha,
    observation.treeSha,
  ];
  if (values.some((value) => !SHA_PATTERN.test(String(value ?? '')))) return 'invalid observation';
  if (observation.baseSha.toLowerCase() !== expectedBase
    && observation.headSha.toLowerCase() !== expectedHead) return 'base and head mismatch';
  if (observation.baseSha.toLowerCase() !== expectedBase) return 'base mismatch';
  if (observation.headSha.toLowerCase() !== expectedHead) return 'head mismatch';
  return null;
}

/**
 * Resolve one exact control-base/event-head merge identity. Failed or mismatched
 * observations are retried twice, then remain fail-closed with no output files.
 *
 * The event head must match exactly — that is the untrusted side of the merge and is never
 * negotiable. The first parent is allowed to be the base branch's *live* protected tip when
 * the base advanced after dispatch (`merge-ref-moved`): GitHub regenerates the merge ref
 * against the current base, so demanding the dispatch-time control base turned every base
 * push during a run into a planner error (CI-03 #2327). Both accepted first parents are
 * protected-branch heads, so no untrusted content enters the binding.
 */
export async function resolveMergeRef({
  expectedBase,
  expectedHead,
  mergeOutput,
  treeOutput,
  observe,
  resolveBaseTip = null,
  sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  log = () => {},
}) {
  const normalizedBase = requireSha(expectedBase, 'expected base');
  const normalizedHead = requireSha(expectedHead, 'expected head');
  const mergeOutputPath = requireOutputPath(mergeOutput, 'merge output path');
  const treeOutputPath = requireOutputPath(treeOutput, 'tree output path');
  if (mergeOutputPath === treeOutputPath) throw new Error('merge and tree output paths must differ');
  if (typeof observe !== 'function') throw new Error('merge-ref observer is required');

  removeOutputs([mergeOutputPath, treeOutputPath]);
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
      // only if — it is the live tip of the protected base ref.
      try {
        const tip = await resolveBaseTip();
        if (SHA_PATTERN.test(String(tip ?? '')) && String(tip).toLowerCase() === observation.baseSha.toLowerCase()) {
          movedBase = String(tip).toLowerCase();
          finalReason = null;
        } else {
          finalReason = 'base mismatch (not the live protected base tip)';
        }
      } catch {
        finalReason = 'base mismatch (the protected base tip could not be read)';
      }
    }

    if (finalReason === null) {
      publishOutputs(observation, mergeOutputPath, treeOutputPath);
      if (movedBase) {
        log(`merge-ref-moved — the base advanced from ${normalizedBase} to the live protected tip ${movedBase} after dispatch; the event head matched exactly on attempt ${attempt}/${MAX_ATTEMPTS}`);
      } else {
        log(`merge ref matched the control base and event head on attempt ${attempt}/${MAX_ATTEMPTS}`);
      }
      return { ...observation, mergeRefMoved: movedBase !== null, baseTipSha: movedBase };
    }

    if (attempt < MAX_ATTEMPTS) {
      log(`merge ref attempt ${attempt}/${MAX_ATTEMPTS} did not match: ${finalReason}; retrying`);
      await sleep(RETRY_DELAY_MS);
    }
  }

  removeOutputs([mergeOutputPath, treeOutputPath]);
  throw new Error(`merge ref resolution failed closed after ${MAX_ATTEMPTS} attempts: ${finalReason}`);
}

function parseArgs(argv) {
  const args = {
    pullRequestNumber: null,
    expectedBase: null,
    expectedHead: null,
    baseRef: null,
    mergeOutput: null,
    treeOutput: null,
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
    observe: () => observeMergeRef({
      pullRequestNumber: args.pullRequestNumber,
      token: process.env.GH_TOKEN,
    }),
    resolveBaseTip: args.baseRef
      ? () => observeBaseTip({ baseRef: args.baseRef, token: process.env.GH_TOKEN })
      : null,
    log: (message) => process.stderr.write(`${message}
`),
  });
  if (args.noteOutput && resolved.mergeRefMoved) {
    mkdirSync(dirname(args.noteOutput), { recursive: true });
    writeFileSync(
      args.noteOutput,
      `merge-ref-moved: the base advanced from ${String(args.expectedBase).toLowerCase()} to ${resolved.baseTipSha} after dispatch; the merge ref was regenerated against the live protected base tip and the event head matched exactly
`,
      { encoding: 'utf8' },
    );
  }
}

if (process.argv[1] && /resolve-merge-ref\.mjs$/i.test(process.argv[1])) {
  main().catch((error) => {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    process.exitCode = 1;
  });
}
