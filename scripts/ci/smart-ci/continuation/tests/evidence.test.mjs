import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import { fingerprint } from '../core/contracts.mjs';
import { signRecord, verifyEvidence, assertProducer } from '../core/evidence.mjs';
import { request, record, producerExpected, producerObserved, oid } from './fixtures.mjs';
const keys = generateKeyPairSync('ed25519'), other = generateKeyPairSync('ed25519');
const setup = () => {
  const fp = fingerprint(request()), r = record(fp), envelope = signRecord(r, keys.privateKey);
  return { fp, envelope, options: { now: 1100, trustedKeys: { 'test-key': keys.publicKey }, issuer: 'protected-collector',
    ledgerComplete: true, revokedInputKeys: [], disabledTasks: [], fullQualification: false } };
};
test('valid signed exact-input evidence is reusable with original lifetime', () => {
  const { fp, envelope, options } = setup(), v = verifyEvidence(envelope, fp, options);
  assert.ok(v.reusable); assert.equal(v.origin.completedAt, 1000); assert.equal(v.origin.expiresAt, 4600);
});
test('frontend fix can retain successful backend despite prior failed sibling', () => {
  const { envelope, options } = setup(), r = request(); r.state.entries[1].oid = oid('b'); r.state.commit = oid('3'); r.state.tree = oid('4');
  assert.ok(verifyEvidence(envelope, fingerprint(r), options).reusable);
});
const changes = {
  tamperedKey: x => x.envelope.record.inputKey = `sha256:${'a'.repeat(64)}`,
  tamperedExpiry: x => x.envelope.record.expiresAt += 50,
  wrongKey: x => x.options.trustedKeys['test-key'] = other.publicKey,
  noKey: x => x.options.trustedKeys = {},
  wrongIssuer: x => x.options.issuer = 'different',
  wrongRepo: x => x.fp.repositoryId = '9999',
  wrongTask: x => x.fp.taskId = 'frontend',
  wrongPolicy: x => x.fp.policyDigest = `sha256:${'a'.repeat(64)}`,
  wrongGraph: x => x.fp.graphDigest = `sha256:${'a'.repeat(64)}`,
  wrongPlatform: x => x.fp.platform = 'windows-x64',
  newInputs: x => x.fp.key = `sha256:${'b'.repeat(64)}`,
  future: x => x.options.now = 999,
  expired: x => x.options.now = 4600,
  revoked: x => x.options.revokedInputKeys.push(x.fp.key),
  circuit: x => x.options.disabledTasks.push(x.fp.taskId),
  incompleteLedger: x => x.options.ledgerComplete = false,
  missingLedger: x => delete x.options.revokedInputKeys,
  fullQualification: x => x.options.fullQualification = true,
  ineligible: x => x.fp.reusable = false,
  signature: x => x.envelope.signature = 'abc',
  missingClock: x => delete x.options.now,
  shorterTTL: x => x.fp.ttlSeconds = 50,
  extraField: x => x.envelope.record.claimedTrusted = true
};
for (const [name, mutate] of Object.entries(changes)) test(`${name}: fallback to fresh execution`, () => {
  const x = setup(); mutate(x); assert.equal(verifyEvidence(x.envelope, x.fp, x.options).reusable, false);
});
test('legitimately signed retry is not reusable', () => {
  const x = setup(); x.envelope.record.attempt = 2; x.envelope = signRecord(x.envelope.record, keys.privateKey);
  assert.equal(verifyEvidence(x.envelope, x.fp, x.options).reusable, false);
});
for (const [name, mutate] of Object.entries({ zeroTests: r => r.executedTests = 0, skipped: r => r.conclusion = 'skipped',
  failed: r => r.conclusion = 'failure', badCommit: r => r.commit = 'HEAD', badLifetime: r => r.expiresAt = r.completedAt })) {
  test(`issuer rejects ${name}`, () => { const r = record(fingerprint(request())); mutate(r); assert.throws(() => signRecord(r, keys.privateKey)); });
}
test('wrong key algorithm is rejected', () => assert.throws(() => signRecord(record(fingerprint(request())), { asymmetricKeyType: 'rsa' })));
test('producer exact execution accepted even if unrelated workflow sibling fails', () => {
  const e = producerExpected(), o = producerObserved(e); o.workflowConclusion = 'failure'; assert.ok(assertProducer(e, o));
});
for (const field of ['repositoryId', 'workflowId', 'workflowPath', 'workflowRevision', 'commit', 'tree', 'taskId', 'inputKey', 'policyDigest', 'graphDigest', 'platform', 'commandDigest', 'environmentDigest']) {
  test(`producer rejects mismatch ${field}`, () => { const e = producerExpected(), o = producerObserved(e); o[field] = 'other'; assert.throws(() => assertProducer(e, o)); });
}
for (const [field, value] of [['workflowDefinitionTrusted', false], ['inputManifestRecomputed', false], ['status', 'queued'],
  ['conclusion', 'cancelled'], ['attempt', 2], ['earlierFailure', true], ['allowFailure', true], ['coverageComplete', false],
  ['executedChecks', 0], ['executedTests', 0]]) {
  test(`producer rejects ${field}=${value}`, () => { const e = producerExpected(), o = producerObserved(e); o[field] = value; assert.throws(() => assertProducer(e, o)); });
}
