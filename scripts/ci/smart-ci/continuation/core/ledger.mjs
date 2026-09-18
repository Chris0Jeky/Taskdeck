import { mkdirSync, openSync, readFileSync, writeFileSync, closeSync, fsyncSync, lstatSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { canonical, hash, invariant, isDigest, isGitId } from './primitives.mjs';

export const GENESIS = hash({ domain: 'ci.ledger.genesis.v1' });
const ref = value => typeof value === 'string' && /^[a-z0-9-]+$/.test(value);
function validateEvent(e) {
  invariant(e && Object.keys(e).every(k => ['kind', 'at', 'taskId', 'inputKey', 'reason', 'baselineId', 'policyDigest', 'tree', 'covers', 'complete', 'conclusion'].includes(k)), 'unknown ledger event field');
  invariant(Number.isSafeInteger(e.at) && e.at >= 0, 'invalid ledger timestamp');
  if (e.kind === 'revoke') invariant(isDigest(e.inputKey) && typeof e.reason === 'string' && e.reason.length > 0, 'invalid revocation');
  else if (e.kind === 'trip') invariant(ref(e.taskId) && typeof e.reason === 'string' && e.reason.length > 0, 'invalid circuit event');
  else if (e.kind === 'full') invariant(isDigest(e.baselineId) && isDigest(e.policyDigest) && isGitId(e.tree) && e.complete === true && e.conclusion === 'success' && Array.isArray(e.covers) && e.covers.length > 0 && new Set(e.covers).size === e.covers.length && e.covers.every(ref), 'invalid full baseline');
  else if (e.kind === 'recover') invariant(ref(e.taskId) && isDigest(e.baselineId), 'invalid recovery');
  else if (e.kind === 'merge') invariant(isGitId(e.tree) && isDigest(e.policyDigest), 'invalid merge exposure');
  else throw new Error('unknown ledger event kind');
  return e;
}

/** Hash chain detects corruption/rollback only against an independently protected anchor. */
export function decodeLedger(text, expectedAnchor, { maxBytes = 8 * 1024 * 1024 } = {}) {
  invariant(isDigest(expectedAnchor) && typeof text === 'string' && Buffer.byteLength(text) <= maxBytes, 'trusted anchor/bounded ledger required');
  invariant(text === '' || text.endsWith('\n'), 'partial ledger write');
  const records = [], lines = text === '' ? [] : text.slice(0, -1).split('\n');
  let anchor = GENESIS, previousAt = 0;
  for (const line of lines) {
    const r = JSON.parse(line);
    invariant(r && Object.keys(r).sort().join(',') === 'digest,event,previous,sequence', 'invalid ledger record');
    validateEvent(r.event);
    invariant(r.sequence === records.length + 1 && r.previous === anchor && r.event.at >= previousAt, 'ledger order/binding mismatch');
    invariant(r.digest === hash({ domain: 'ci.ledger.record.v1', sequence: r.sequence, previous: r.previous, event: r.event }), 'ledger corruption');
    records.push(r); anchor = r.digest; previousAt = r.event.at;
  }
  invariant(anchor === expectedAnchor, 'ledger rollback or stale anchor');
  return { records, anchor };
}

/** Single-host reference store. Directory and external anchor are controller-owned, never PR writable. */
export function appendLedger(directory, expectedAnchor, event) {
  validateEvent(event); invariant(isDigest(expectedAnchor), 'trusted expected anchor required');
  const dir = resolve(directory); mkdirSync(dir, { recursive: true, mode: 0o700 });
  invariant(lstatSync(dir).isDirectory() && !lstatSync(dir).isSymbolicLink(), 'ledger directory must be ordinary trusted storage');
  const lock = join(dir, 'writer.lock'), path = join(dir, 'events.jsonl');
  let lockFd;
  try { lockFd = openSync(lock, 'wx', 0o600); }
  catch { throw new Error('ledger writer busy or storage unavailable'); }
  try {
    let text = '';
    try {
      const stat = lstatSync(path); invariant(stat.isFile() && !stat.isSymbolicLink() && stat.size <= 8 * 1024 * 1024, 'invalid ledger file');
      text = readFileSync(path, 'utf8');
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
    const ledger = decodeLedger(text, expectedAnchor);
    invariant(event.at >= (ledger.records.at(-1)?.event.at ?? 0), 'ledger clock moved backwards');
    const unsigned = { sequence: ledger.records.length + 1, previous: expectedAnchor, event };
    const record = { ...unsigned, digest: hash({ domain: 'ci.ledger.record.v1', ...unsigned }) };
    // Reject semantically invalid recovery before it can poison the durable chain.
    ledgerState({ records: [...ledger.records, record], anchor: record.digest });
    const next = canonical(record) + '\n';
    invariant(Buffer.byteLength(text) + Buffer.byteLength(next) <= 8 * 1024 * 1024, 'ledger capacity reached');
    const fd = openSync(path, 'a', 0o600);
    try { writeFileSync(fd, next); fsyncSync(fd); } finally { closeSync(fd); }
    return record; // caller must publish digest through a protected compare-and-swap anchor store
  } finally { closeSync(lockFd); rmSync(lock); }
}

/** Reducer does not erase historical revocations. Recovery requires a newer complete covering baseline. */
export function ledgerState(ledger) {
  const revoked = new Set(), disabled = new Map(), fulls = new Map(); let baseline = null, mergesSinceFull = 0;
  for (const { event: e } of ledger.records) {
    if (e.kind === 'revoke') revoked.add(e.inputKey);
    else if (e.kind === 'trip') disabled.set(e.taskId, e.at);
    else if (e.kind === 'full') { invariant(!fulls.has(e.baselineId), 'duplicate full baseline identity'); fulls.set(e.baselineId, e); baseline = e; mergesSinceFull = 0; }
    else if (e.kind === 'merge') mergesSinceFull++;
    else if (e.kind === 'recover') {
      const full = fulls.get(e.baselineId), trip = disabled.get(e.taskId);
      invariant(trip !== undefined && full && full.at > trip && full.at <= e.at && full.covers.includes(e.taskId), 'recovery lacks a newer covering full baseline');
      disabled.delete(e.taskId);
    }
  }
  return { anchor: ledger.anchor, revokedInputKeys: [...revoked].sort(), disabledTasks: [...disabled.keys()].sort(), baseline, mergesSinceFull };
}

/** Freshness plus merge exposure. Missing/partial/untrusted state always forces full qualification. */
export function qualificationRequired({ state, anchorVerified, now, policyDigest, universe, maxAgeSeconds, maxMerges, mandatory = false }) {
  try {
    invariant(!mandatory && anchorVerified === true && state && isDigest(state.anchor), 'mandatory full or unverifiable state');
    invariant(Number.isSafeInteger(now) && Number.isSafeInteger(maxAgeSeconds) && maxAgeSeconds > 0 && Number.isSafeInteger(maxMerges) && maxMerges > 0, 'invalid qualification budget');
    invariant(Array.isArray(universe) && universe.length > 0 && new Set(universe).size === universe.length, 'complete qualification universe required');
    const b = state.baseline;
    invariant(b?.complete === true && b.conclusion === 'success' && b.policyDigest === policyDigest && universe.every(t => b.covers.includes(t)), 'missing full baseline coverage');
    invariant(b.at <= now && now - b.at < maxAgeSeconds && Number.isSafeInteger(state.mergesSinceFull) && state.mergesSinceFull >= 0 && state.mergesSinceFull < maxMerges, 'qualification age/exposure exhausted');
    invariant(state.disabledTasks.length === 0, 'audit circuit is open');
    return { required: false, reason: 'verified baseline within age and exposure limits' };
  } catch (error) { return { required: true, reason: error.message }; }
}
