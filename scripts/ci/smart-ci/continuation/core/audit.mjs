import { createHmac } from 'node:crypto';
import { invariant } from './primitives.mjs';

/** A frozen pre-execution plan is compared to every full-oracle outcome, not just final green reruns. */
export function auditSelection({ selected, universe, outcomes, identitiesVerified }) {
  invariant(new Set(universe).size === universe.length && universe.length > 0, 'invalid universe');
  invariant(new Set(selected).size === selected.length && selected.every(id => universe.includes(id)), 'invalid selection');
  if (identitiesVerified !== true) return { status: 'unusable', forceFull: true, misses: [], reason: 'unbound oracle' };
  const missing = universe.filter(id => !outcomes.some(o => o.taskId === id));
  const invalid = outcomes.some(o => !universe.includes(o.taskId) || !['success', 'failure'].includes(o.conclusion) ||
    !Number.isSafeInteger(o.attempt) || o.attempt < 1);
  if (missing.length > 0 || invalid) return { status: 'unusable', forceFull: true, misses: [], reason: 'incomplete oracle' };
  const misses = [...new Set(outcomes.filter(o => o.conclusion === 'failure' && !selected.includes(o.taskId)).map(o => o.taskId))].sort();
  return { status: misses.length > 0 ? 'miss' : 'observed', forceFull: misses.length > 0, misses,
    detectedFailures: outcomes.filter(o => o.conclusion === 'failure' && selected.includes(o.taskId)).length };
}

/** Trusted, reproducible sampling. Salt is controller-only; user-controlled SHA grinding must not pick the audit. */
export function sampledAudit({ repositoryId, tree, epoch, secret, rate }) {
  invariant(typeof secret === 'string' && secret.length >= 32, 'controller-only audit salt required');
  invariant(Number.isFinite(rate) && rate >= 0 && rate <= 1, 'invalid audit rate');
  invariant([repositoryId, tree, epoch].every(x => typeof x === 'string' && x.length > 0), 'audit binding required');
  const bytes = createHmac('sha256', secret).update(JSON.stringify([repositoryId, tree, epoch])).digest();
  return bytes.readUInt32BE(0) / 2 ** 32 < rate;
}

/** One-sided exact upper bound for 0 misses in n independent Bernoulli opportunities. NOT a correctness proof. */
export function zeroMissUpperBound(n, confidence = 0.95) {
  invariant(Number.isSafeInteger(n) && n > 0, 'positive sample required');
  invariant(confidence > 0 && confidence < 1, 'invalid confidence');
  return 1 - (1 - confidence) ** (1 / n);
}

/** Safety net freshness. Time alone cannot stand in for unchanged input or a successful complete baseline. */
export function requiresFullQualification({ now, baseline, policyDigest, force, event, risk }) {
  if (force || ['release', 'weekly'].includes(event) || risk === 'R4') return { required: true, reason: 'mandatory-full' };
  if (!baseline || baseline.conclusion !== 'success' || baseline.complete !== true || baseline.policyDigest !== policyDigest)
    return { required: true, reason: 'no-valid-full-baseline' };
  if (!Number.isSafeInteger(now) || !Number.isSafeInteger(baseline.completedAt) || !Number.isSafeInteger(baseline.maxAgeSeconds) ||
    baseline.maxAgeSeconds <= 0 || baseline.completedAt > now || now - baseline.completedAt >= baseline.maxAgeSeconds)
    return { required: true, reason: 'stale-full-baseline' };
  return { required: false, reason: 'fresh-full-baseline' };
}
