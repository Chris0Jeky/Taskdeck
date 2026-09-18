import { sign, verify } from 'node:crypto';
import { canonical, invariant, isDigest, isGitId } from './primitives.mjs';

// Auxiliary task attestations. Taskdeck's ci-run.v1 remains the ONLY gate receipt.
// The issuer is a protected collector, not the process that executes repository code.
const FIELDS = ['version', 'issuer', 'keyId', 'repositoryId', 'taskId', 'inputKey', 'policyDigest',
  'graphDigest', 'platform', 'commit', 'tree', 'workflowId', 'runId', 'jobId', 'attempt',
  'conclusion', 'executedTests', 'completedAt', 'expiresAt'];
function validateRecord(record) {
  invariant(record && typeof record === 'object' && !Array.isArray(record), 'invalid record');
  invariant(Object.keys(record).length === FIELDS.length && FIELDS.every(k => Object.hasOwn(record, k)), 'unexpected record shape');
  invariant(record.version === 1, 'unsupported task attestation version');
  for (const key of ['issuer', 'keyId', 'taskId', 'platform']) invariant(typeof record[key] === 'string' && record[key].length > 0, `missing ${key}`);
  for (const key of ['repositoryId', 'workflowId', 'runId', 'jobId']) invariant(typeof record[key] === 'string' && /^[1-9]\d*$/.test(record[key]), `invalid ${key}`);
  for (const key of ['inputKey', 'policyDigest', 'graphDigest']) invariant(isDigest(record[key]), `invalid ${key}`);
  invariant(isGitId(record.commit) && isGitId(record.tree), 'invalid source binding');
  invariant(Number.isSafeInteger(record.attempt) && record.attempt >= 1, 'invalid attempt');
  invariant(record.conclusion === 'success', 'only successful individual jobs are attestable');
  invariant(record.executedTests === null || (Number.isSafeInteger(record.executedTests) && record.executedTests > 0), 'zero/invalid executed tests');
  invariant(Number.isSafeInteger(record.completedAt) && Number.isSafeInteger(record.expiresAt) && record.completedAt >= 0 && record.expiresAt > record.completedAt, 'invalid record lifetime');
  return record;
}

/**
 * Bind normalized, authenticated provider observations to an immutable expected execution.
 * IMPORTANT: observations must come from a trusted REST/API collector; a PR JSON artifact
 * that claims to contain this object has no authority. A failed sibling does not invalidate
 * an independently successful job; a failed/cancelled/neutral/skipped task does.
 */
export function assertProducer(expected, observed) {
  const keys = ['repositoryId', 'workflowId', 'workflowPath', 'workflowRevision', 'commit', 'tree',
    'taskId', 'inputKey', 'policyDigest', 'graphDigest', 'platform', 'commandDigest', 'environmentDigest'];
  for (const key of keys) invariant(typeof expected?.[key] === 'string' && expected[key].length > 0 && observed?.[key] === expected[key], `producer binding mismatch: ${key}`);
  invariant(observed.workflowDefinitionTrusted === true && observed.inputManifestRecomputed === true,
    'collector has not verified workflow/input provenance');
  invariant(observed.status === 'completed' && observed.conclusion === 'success', 'job did not complete successfully');
  invariant(observed.attempt === 1 && observed.earlierFailure === false, 'rerun cannot erase a failure');
  invariant(observed.allowFailure === false && observed.coverageComplete === true, 'partial or continue-on-error execution');
  invariant(Number.isSafeInteger(observed.executedChecks) && observed.executedChecks > 0, 'empty execution');
  if (expected.requiresTests) invariant(Number.isSafeInteger(observed.executedTests) && observed.executedTests > 0, 'empty test discovery');
  return true;
}

/** Call only after assertProducer, from a protected issuer. No keys belong in PR jobs. */
export function signRecord(record, privateKey) {
  validateRecord(record);
  invariant(privateKey?.asymmetricKeyType === 'ed25519', 'Ed25519 private key required');
  const bytes = Buffer.from(canonical({ domain: 'ci.task-attestation.v1', record }));
  return { record, signature: sign(null, bytes, privateKey).toString('base64') };
}

/** Invalid, stale, revoked or unverifiable evidence is a cache MISS, never success. */
export function verifyEvidence(envelope, current, options) {
  try {
    invariant(options && Number.isSafeInteger(options.now), 'verification clock required');
    invariant(options.fullQualification !== true, 'full qualification bypasses reuse');
    invariant(current.reusable === true, 'current input contract is not reusable');
    invariant(options.ledgerComplete === true, 'complete trusted revocation view required');
    const record = validateRecord(envelope?.record);
    const key = options.trustedKeys?.[record.keyId];
    invariant(key?.asymmetricKeyType === 'ed25519', 'untrusted signing key');
    invariant(record.issuer === options.issuer, 'untrusted issuer');
    invariant(typeof envelope.signature === 'string' && /^[A-Za-z0-9+/]{86}==$/.test(envelope.signature), 'invalid signature encoding');
    invariant(verify(null, Buffer.from(canonical({ domain: 'ci.task-attestation.v1', record })), key,
      Buffer.from(envelope.signature, 'base64')), 'invalid signature');
    const pairs = { repositoryId: 'repositoryId', taskId: 'taskId', inputKey: 'key',
      policyDigest: 'policyDigest', graphDigest: 'graphDigest', platform: 'platform' };
    for (const [field, currentField] of Object.entries(pairs)) invariant(record[field] === current[currentField], `evidence mismatch: ${field}`);
    invariant(record.attempt === 1, 'rerun evidence is not reusable');
    invariant(record.completedAt <= options.now, 'future evidence');
    invariant(record.expiresAt > options.now, 'expired evidence');
    invariant(options.now - record.completedAt < current.ttlSeconds, 'contract TTL exceeded');
    invariant(record.expiresAt - record.completedAt <= current.ttlSeconds, 'issuer exceeded contract TTL');
    invariant(Array.isArray(options.revokedInputKeys) && !options.revokedInputKeys.includes(current.key), 'revoked input evidence');
    invariant(Array.isArray(options.disabledTasks) && !options.disabledTasks.includes(current.taskId), 'task circuit breaker open');
    // No renewal when reusing: return the ORIGINAL completion and expiry unchanged.
    return { reusable: true, reason: 'exact-input-attestation', origin: {
      commit: record.commit, tree: record.tree, runId: record.runId, jobId: record.jobId,
      completedAt: record.completedAt, expiresAt: record.expiresAt
    } };
  } catch (error) {
    return { reusable: false, reason: error.message };
  }
}
