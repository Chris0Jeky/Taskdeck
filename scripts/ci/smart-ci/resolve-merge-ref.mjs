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
const REPOSITORY_PATTERN = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/;
const GITHUB_API_VERSION = '2022-11-28';
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

/**
 * Prove that one commit is in the exact named repository history leading to another.
 * The compare API avoids shallow-checkout ambiguity. Only validated repository and SHA
 * identifiers enter the URL; the read-only token stays in the Authorization header.
 */
export async function verifyBaseHistoryAncestor({
  repository,
  ancestorSha,
  descendantSha,
  token = process.env.GH_TOKEN,
  request = globalThis.fetch,
}) {
  const repositoryName = String(repository ?? '');
  if (!REPOSITORY_PATTERN.test(repositoryName)) {
    throw new Error('GITHUB_REPOSITORY must be an owner/name pair');
  }
  const [owner, name] = repositoryName.split('/');
  if (owner === '.' || owner === '..' || name === '.' || name === '..') {
    throw new Error('GITHUB_REPOSITORY must contain ordinary owner and repository names');
  }
  const ancestor = requireSha(ancestorSha, 'base history ancestor');
  const descendant = requireSha(descendantSha, 'base history descendant');
  if (ancestor === descendant) return true;
  if (typeof token !== 'string' || token.length === 0) {
    throw new Error('GH_TOKEN is required for the read-only base history check');
  }
  if (typeof request !== 'function') throw new Error('a GitHub compare request function is required');

  const url = `https://api.github.com/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/compare/${ancestor}...${descendant}`;
  const response = await request(url, {
    method: 'GET',
    redirect: 'error',
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': GITHUB_API_VERSION,
    },
  });
  if (!response || response.ok !== true) {
    const status = Number.isInteger(response && response.status) ? response.status : 'unknown';
    throw new Error(`GitHub compare could not verify base history (status ${status})`);
  }
  if (typeof response.json !== 'function') {
    throw new Error('GitHub compare returned an unreadable response');
  }
  const payload = await response.json();
  const baseCommit = String(payload && payload.base_commit ? payload.base_commit.sha ?? '' : '').toLowerCase();
  const mergeBase = String(payload && payload.merge_base_commit ? payload.merge_base_commit.sha ?? '' : '').toLowerCase();
  return payload && payload.status === 'ahead'
    && Number.isInteger(payload.ahead_by) && payload.ahead_by > 0
    && Number.isInteger(payload.behind_by) && payload.behind_by === 0
    && baseCommit === ancestor
    && mergeBase === ancestor;
}

function removeOutputs(paths) {
  for (const path of paths) rmSync(path, { force: true });
}

function publishEntries(entries, cleanupPaths) {
  const outputPaths = entries.map(([path]) => path);
  const temporaryPaths = outputPaths.map((path) => `${path}.tmp-${process.pid}`);

  try {
    for (const path of outputPaths) mkdirSync(dirname(path), { recursive: true });
    removeOutputs([...temporaryPaths, ...cleanupPaths]);
    entries.forEach(([, value], index) => {
      writeFileSync(temporaryPaths[index], `${value}\n`, { encoding: 'utf8', flag: 'wx' });
    });
    entries.forEach(([path], index) => renameSync(temporaryPaths[index], path));
  } catch (error) {
    removeOutputs([...temporaryPaths, ...cleanupPaths]);
    throw error;
  }
}

function publishQualifiedOutputs(observation, outputs, cleanupPaths) {
  const entries = [
    [outputs.merge, observation.mergeSha],
    [outputs.tree, observation.treeSha],
    [outputs.mergeBase, observation.mergeBaseSha],
    [outputs.mergeBaseTip, observation.mergeBaseTipSha ?? 'null'],
  ];
  entries.push([outputs.qualification, 'qualified']);
  publishEntries(entries, cleanupPaths);
}

function publishUnqualifiedStatus(qualificationOutput, cleanupPaths) {
  if (!qualificationOutput) throw new Error('qualification output is required for an unqualified merge ref');
  publishEntries([[qualificationOutput, 'stale-base-unqualified']], cleanupPaths);
}

function observationReason(observation, expectedHead) {
  if (!observation || typeof observation !== 'object') return 'merge ref unavailable';
  const values = [
    observation.mergeSha,
    observation.mergeBaseSha,
    observation.headSha,
    observation.treeSha,
  ];
  if (values.some((value) => !SHA_PATTERN.test(String(value ?? '')))) return 'invalid observation';
  if (observation.headSha.toLowerCase() !== expectedHead) return 'head mismatch';
  return null;
}

/**
 * Resolve one exact named-base/event-head merge identity. Failed or mismatched
 * observations are retried twice, then remain fail-closed with no output files.
 *
 * The event head must match exactly. The first parent qualifies only when it equals the
 * authenticated tip of the event's named base ref. A retained first parent that is positively
 * proven to be in that tip's history is classified as stale and publishes no merge identity.
 * The expected base remains control-code provenance; it need not be related to a non-default
 * or stacked PR base.
 */
export async function resolveMergeRef({
  expectedBase,
  expectedHead,
  mergeOutput,
  treeOutput,
  mergeBaseOutput,
  mergeBaseTipOutput,
  qualificationOutput,
  observe,
  resolveBaseTip = null,
  verifyBaseAncestor = null,
  sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  log = () => {},
}) {
  const normalizedBase = requireSha(expectedBase, 'expected base');
  const normalizedHead = requireSha(expectedHead, 'expected head');
  const mergeOutputPath = requireOutputPath(mergeOutput, 'merge output path');
  const treeOutputPath = requireOutputPath(treeOutput, 'tree output path');
  const mergeBaseOutputPath = requireOutputPath(mergeBaseOutput, 'merge base output path');
  const mergeBaseTipOutputPath = requireOutputPath(mergeBaseTipOutput, 'merge base tip output path');
  const qualificationOutputPath = requireOutputPath(qualificationOutput, 'merge ref qualification output path');
  const outputPaths = [mergeOutputPath, treeOutputPath, mergeBaseOutputPath, mergeBaseTipOutputPath, qualificationOutputPath];
  if (new Set(outputPaths).size !== outputPaths.length) throw new Error('merge identity output paths must differ');
  if (typeof observe !== 'function') throw new Error('merge-ref observer is required');
  if (typeof resolveBaseTip !== 'function') throw new Error('named base-ref resolver is required');
  if (typeof verifyBaseAncestor !== 'function') throw new Error('base-history verifier is required');

  removeOutputs(outputPaths);
  let finalReason = 'merge ref unavailable';

  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt += 1) {
    let observation = null;
    let liveBaseTip = null;
    try {
      observation = await observe();
      finalReason = observationReason(observation, normalizedHead);
    } catch {
      finalReason = 'merge ref unavailable';
    }

    if (finalReason === null) {
      // The head already matched exactly. Authenticate the named base tip before deciding
      // whether the observed first parent is current, retained, or unrelated.
      try {
        const tip = await resolveBaseTip();
        liveBaseTip = requireSha(tip, 'named base branch tip');
        finalReason = observation.mergeBaseSha.toLowerCase() === liveBaseTip
          ? null
          : 'base mismatch (retained first parent differs from the named base branch tip)';
      } catch {
        finalReason = 'base mismatch (the base branch tip could not be read)';
      }
    }

    if (finalReason === null) {
      const distinctNamedBase = liveBaseTip !== normalizedBase;
      const resolved = {
        ...observation,
        mergeRefMoved: distinctNamedBase,
        mergeBaseTipSha: distinctNamedBase ? liveBaseTip : null,
        mergeRefQualification: 'qualified',
      };
      publishQualifiedOutputs(resolved, {
        merge: mergeOutputPath,
        tree: treeOutputPath,
        mergeBase: mergeBaseOutputPath,
        mergeBaseTip: mergeBaseTipOutputPath,
        qualification: qualificationOutputPath,
      }, outputPaths);
      if (distinctNamedBase) {
        log(`merge-ref-moved: named base tip ${liveBaseTip} differed from control checkout ${normalizedBase}; the merge ref matched that named tip and the exact event head on attempt ${attempt}/${MAX_ATTEMPTS}`);
      } else {
        log(`merge ref matched the named base tip and exact event head on attempt ${attempt}/${MAX_ATTEMPTS}`);
      }
      return resolved;
    }

    if (attempt === MAX_ATTEMPTS
      && finalReason === 'base mismatch (retained first parent differs from the named base branch tip)'
      && observation
      && liveBaseTip) {
      try {
        const retainedBase = observation.mergeBaseSha.toLowerCase();
        const retainedIsAncestor = await verifyBaseAncestor(retainedBase, liveBaseTip);
        if (!retainedIsAncestor) {
          finalReason = 'base mismatch (retained first parent was not proven in named base history)';
        } else {
          const resolved = {
            ...observation,
            mergeRefMoved: false,
            mergeBaseTipSha: liveBaseTip,
            mergeRefQualification: 'stale-base-unqualified',
          };
          publishUnqualifiedStatus(qualificationOutputPath, outputPaths);
          log(`merge-ref-unqualified: the exact event head matched, but retained first parent ${retainedBase} differs from current named base tip ${liveBaseTip} and remains in its history; no merge identity was qualified`);
          return resolved;
        }
      } catch {
        finalReason = 'base mismatch (base history could not be verified)';
      }
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
 * Write a planner diagnostic for a distinct named-base tip or a retained stale merge ref.
 * Always LF-terminated so the note does not depend on the checkout's line endings.
 */
export function writeMergeRefNote(noteOutput, expectedBase, resolved) {
  mkdirSync(dirname(noteOutput), { recursive: true });
  const note = resolved.mergeRefQualification === 'stale-base-unqualified'
    ? `merge-ref-unqualified: exact event head matched, but retained first parent ${resolved.mergeBaseSha} differs from current named base tip ${resolved.mergeBaseTipSha} and remains in its history; no merge or tree qualification was published`
    : `merge-ref-moved: named base tip ${resolved.mergeBaseTipSha} differed from control checkout ${String(expectedBase).toLowerCase()}; the merge ref matched that named tip and the exact event head`;
  writeFileSync(
    noteOutput,
    `${note}\n`,
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
    qualificationOutput: null,
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
      case '--qualification-out': args.qualificationOutput = next(); break;
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
    qualificationOutput: args.qualificationOutput,
    observe: () => observeMergeRef({
      pullRequestNumber: args.pullRequestNumber,
      token: process.env.GH_TOKEN,
    }),
    resolveBaseTip: args.baseRef
      ? () => observeBaseTip({ baseRef: args.baseRef, token: process.env.GH_TOKEN })
      : null,
    verifyBaseAncestor: args.baseRef
      ? (ancestorSha, descendantSha) => verifyBaseHistoryAncestor({
        repository: process.env.GITHUB_REPOSITORY,
        ancestorSha,
        descendantSha,
        token: process.env.GH_TOKEN,
      })
      : null,
    log: (message) => process.stderr.write(`${message}\n`),
  });
  if (args.noteOutput && (resolved.mergeRefMoved || resolved.mergeRefQualification === 'stale-base-unqualified')) {
    writeMergeRefNote(args.noteOutput, args.expectedBase, resolved);
  }
}

if (process.argv[1] && /resolve-merge-ref\.mjs$/i.test(process.argv[1])) {
  main().catch((error) => {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
    process.exitCode = 1;
  });
}
