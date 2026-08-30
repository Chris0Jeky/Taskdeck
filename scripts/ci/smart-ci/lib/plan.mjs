// Smart CI planner and gate evaluator — pure functions (ADR-0066, CI-02 #2326 / CI-03 #2327).
//
// Everything here is deterministic for identical input and content-free: plans carry
// paths, SHAs, logins, labels, lane ids and reasons — never file contents, PR text or
// log output. The planner FAILS CLOSED: any doubt selects the full conservative plan.

import { createHash } from 'node:crypto';
import { matchesAny } from './glob.mjs';

export const RISK_ORDER = Object.freeze(['R0', 'R1', 'R2', 'R3', 'R4']);
export const TRUST_CLASSES = Object.freeze(['T0', 'T1', 'T2', 'T3', 'T4']);
export const EXECUTION_MODES = Object.freeze(['hosted', 'hybrid', 'self-hosted']);
export const LABEL_ACTIONS = Object.freeze(['escalate-full', 'force-hosted', 'windows-full']);
export const PLAN_SCHEMA_VERSION = 1;

export class PolicyError extends Error {
  constructor(errors) {
    super(`Invalid Smart CI policy:\n- ${errors.join('\n- ')}`);
    this.name = 'PolicyError';
    this.errors = errors;
  }
}

export function policyDigest(policyText) {
  return `sha256:${createHash('sha256').update(policyText).digest('hex')}`;
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.length > 0;
}

function isStringArray(value) {
  return Array.isArray(value) && value.every(isNonEmptyString);
}

/** Structural validation of a policy document. Returns a list of error strings (empty = valid). */
export function validatePolicy(policy) {
  const errors = [];
  if (!policy || typeof policy !== 'object') return ['policy must be an object'];
  if (policy.schemaVersion !== 1) errors.push('schemaVersion must be 1');
  if (!isNonEmptyString(policy.policyId)) errors.push('policyId must be a non-empty string');
  if (!['shadow', 'enforce'].includes(policy.mode)) errors.push('mode must be "shadow" or "enforce"');
  if (!EXECUTION_MODES.includes(policy.defaultExecutionMode)) errors.push(`defaultExecutionMode must be one of ${EXECUTION_MODES.join('|')}`);
  if (policy.failClosed !== true) errors.push('failClosed must be true');
  if (!isStringArray(policy.trustedAssociations) || policy.trustedAssociations.length === 0) errors.push('trustedAssociations must be a non-empty string array');

  const runnerClasses = policy.runnerClasses && typeof policy.runnerClasses === 'object' ? policy.runnerClasses : null;
  if (!runnerClasses || Object.keys(runnerClasses).length === 0) errors.push('runnerClasses must be a non-empty object');
  else {
    for (const [id, entry] of Object.entries(runnerClasses)) {
      if (!entry || typeof entry !== 'object') { errors.push(`runnerClasses.${id} must be an object`); continue; }
      if (!isStringArray(entry.labels) || entry.labels.length === 0) errors.push(`runnerClasses.${id}.labels must be a non-empty string array`);
      if (typeof entry.hosted !== 'boolean') errors.push(`runnerClasses.${id}.hosted must be a boolean`);
    }
  }

  const lanes = policy.lanes && typeof policy.lanes === 'object' ? policy.lanes : null;
  if (!lanes || Object.keys(lanes).length === 0) errors.push('lanes must be a non-empty object');
  else {
    for (const [id, lane] of Object.entries(lanes)) {
      if (!/^[a-z0-9-]+$/.test(id)) errors.push(`lane id "${id}" must be kebab-case`);
      if (!lane || typeof lane !== 'object') { errors.push(`lanes.${id} must be an object`); continue; }
      if (!isNonEmptyString(lane.checkName)) errors.push(`lanes.${id}.checkName must be a non-empty string`);
      if (!isNonEmptyString(lane.family)) errors.push(`lanes.${id}.family must be a non-empty string`);
      if (!runnerClasses || !runnerClasses[lane.runner]) errors.push(`lanes.${id}.runner "${lane.runner}" is not a runner class`);
      if (typeof lane.trustedOnly !== 'boolean') errors.push(`lanes.${id}.trustedOnly must be a boolean`);
      const runnerIsHosted = runnerClasses && runnerClasses[lane.runner] ? runnerClasses[lane.runner].hosted : true;
      if (!runnerIsHosted) {
        if (lane.trustedOnly !== true) errors.push(`lanes.${id} targets a self-hosted class and must be trustedOnly`);
        if (!runnerClasses[lane.hostedFallback] || !runnerClasses[lane.hostedFallback].hosted) errors.push(`lanes.${id}.hostedFallback must name a hosted runner class`);
      }
    }
  }
  const laneIds = lanes ? Object.keys(lanes) : [];
  const checkNames = lanes ? Object.values(lanes).map((lane) => lane && lane.checkName) : [];
  if (new Set(checkNames).size !== checkNames.length) errors.push('lane checkName values must be unique');

  if (!isStringArray(policy.alwaysLanes)) errors.push('alwaysLanes must be a string array');
  else for (const id of policy.alwaysLanes) if (!laneIds.includes(id)) errors.push(`alwaysLanes references unknown lane "${id}"`);

  const riskClasses = policy.riskClasses && typeof policy.riskClasses === 'object' ? policy.riskClasses : null;
  if (!riskClasses) errors.push('riskClasses must be an object');
  else {
    for (const risk of RISK_ORDER) {
      const entry = riskClasses[risk];
      if (!entry || typeof entry !== 'object') { errors.push(`riskClasses.${risk} is missing`); continue; }
      if (!isNonEmptyString(entry.description)) errors.push(`riskClasses.${risk}.description is required`);
      if (!isStringArray(entry.requiredLanes)) errors.push(`riskClasses.${risk}.requiredLanes must be a string array`);
      else for (const id of entry.requiredLanes) if (!laneIds.includes(id)) errors.push(`riskClasses.${risk}.requiredLanes references unknown lane "${id}"`);
      if (typeof entry.fullPlan !== 'boolean') errors.push(`riskClasses.${risk}.fullPlan must be a boolean`);
    }
    if (riskClasses.R4 && riskClasses.R4.fullPlan !== true) errors.push('riskClasses.R4.fullPlan must be true (control changes always qualify fully)');
  }

  const trustClasses = policy.trustClasses && typeof policy.trustClasses === 'object' ? policy.trustClasses : null;
  if (!trustClasses) errors.push('trustClasses must be an object');
  else {
    for (const trust of TRUST_CLASSES) {
      const entry = trustClasses[trust];
      if (!entry || typeof entry !== 'object') { errors.push(`trustClasses.${trust} is missing`); continue; }
      if (!isNonEmptyString(entry.description)) errors.push(`trustClasses.${trust}.description is required`);
      if (typeof entry.selfHostedAllowed !== 'boolean') errors.push(`trustClasses.${trust}.selfHostedAllowed must be a boolean`);
    }
    for (const trust of ['T0', 'T2', 'T3', 'T4']) {
      if (trustClasses[trust] && trustClasses[trust].selfHostedAllowed) errors.push(`trustClasses.${trust}.selfHostedAllowed must be false`);
    }
  }

  if (!isStringArray(policy.controlPaths) || policy.controlPaths.length === 0) errors.push('controlPaths must be a non-empty string array');

  if (!Array.isArray(policy.pathGroups) || policy.pathGroups.length === 0) errors.push('pathGroups must be a non-empty array');
  else {
    const seen = new Set();
    policy.pathGroups.forEach((group, index) => {
      if (!group || typeof group !== 'object') { errors.push(`pathGroups[${index}] must be an object`); return; }
      if (!/^[a-z0-9-]+$/.test(group.id ?? '')) errors.push(`pathGroups[${index}].id must be kebab-case`);
      if (seen.has(group.id)) errors.push(`pathGroups duplicate id "${group.id}"`);
      seen.add(group.id);
      if (!isStringArray(group.patterns) || group.patterns.length === 0) errors.push(`pathGroups.${group.id}.patterns must be a non-empty string array`);
      if (!RISK_ORDER.includes(group.riskFloor)) errors.push(`pathGroups.${group.id}.riskFloor must be one of ${RISK_ORDER.join('|')}`);
      if (!isStringArray(group.lanes ?? [])) errors.push(`pathGroups.${group.id}.lanes must be a string array`);
      else for (const id of group.lanes ?? []) if (!laneIds.includes(id)) errors.push(`pathGroups.${group.id}.lanes references unknown lane "${id}"`);
    });
  }

  if (policy.labels && typeof policy.labels === 'object') {
    for (const [label, action] of Object.entries(policy.labels)) {
      if (!LABEL_ACTIONS.includes(action)) errors.push(`labels["${label}"] must be one of ${LABEL_ACTIONS.join('|')}`);
    }
  } else errors.push('labels must be an object');

  if (!isStringArray(policy.fullEscalationTriggers) || policy.fullEscalationTriggers.length === 0) errors.push('fullEscalationTriggers must be a non-empty string array');
  return errors;
}

/** Trust classification. T0 is the control plane itself and is never assigned to a change. */
export function classifyTrust(input, controlPathsChanged, policy) {
  if (input.eventName === 'push' && String(input.ref ?? '').startsWith('refs/tags/')) return 'T4';
  if (input.isFork === true) return 'T3';
  const login = String(input.actorLogin ?? '');
  if (input.actorType === 'Bot' || /\[bot\]$/i.test(login) || login.length === 0) return 'T3';
  if (!policy.trustedAssociations.includes(String(input.authorAssociation ?? ''))) return 'T3';
  if (controlPathsChanged.length > 0) return 'T2';
  return 'T1';
}

export function matchGroups(changedFiles, policy) {
  const groups = new Map();
  const unmapped = [];
  const controlPathsChanged = [];
  for (const path of changedFiles) {
    if (matchesAny(path, policy.controlPaths)) controlPathsChanged.push(path);
    let matched = false;
    for (const group of policy.pathGroups) {
      if (matchesAny(path, group.patterns)) {
        matched = true;
        if (!groups.has(group.id)) groups.set(group.id, []);
        groups.get(group.id).push(path);
      }
    }
    if (!matched && !matchesAny(path, policy.controlPaths)) unmapped.push(path);
  }
  return { groups, unmapped, controlPathsChanged };
}

function maxRisk(risks) {
  let index = 0;
  for (const risk of risks) index = Math.max(index, RISK_ORDER.indexOf(risk));
  return RISK_ORDER[index];
}

function uniqueSorted(values) {
  return [...new Set(values)].sort();
}

/**
 * Build the deterministic plan.
 * @param {object} input see README: eventName, repository, pullRequestNumber, ref, isDraft, baseSha,
 *   headSha, mergeSha, mergeTreeSha, actorLogin, actorType, authorAssociation, isFork, labels,
 *   changedFiles, changedFilesAvailable, executionMode
 * @param {object} policy parsed policy document
 * @param {string} digest policyDigest() of the policy file bytes
 */
export function buildPlan(input, policy, digest) {
  const policyErrors = validatePolicy(policy);
  if (policyErrors.length > 0) throw new PolicyError(policyErrors);

  const changedFiles = uniqueSorted((input.changedFiles ?? []).map(String).filter((path) => path.length > 0));
  const { groups, unmapped, controlPathsChanged } = matchGroups(changedFiles, policy);
  const trust = classifyTrust(input, controlPathsChanged, policy);
  const labels = uniqueSorted((input.labels ?? []).map(String));

  const escalationReasons = [];
  if (input.changedFilesAvailable !== true) escalationReasons.push('changed-files-unavailable');
  if (!isNonEmptyString(input.baseSha) || !isNonEmptyString(input.headSha)) escalationReasons.push('base-or-head-sha-missing');
  if (unmapped.length > 0) escalationReasons.push('unmapped-path');
  if (controlPathsChanged.length > 0) escalationReasons.push('control-path-change');

  let forceHosted = false;
  let windowsFull = false;
  for (const label of labels) {
    const action = policy.labels[label];
    if (action === 'escalate-full') escalationReasons.push(`label:${label}`);
    if (action === 'force-hosted') forceHosted = true;
    if (action === 'windows-full') windowsFull = true;
  }

  const groupEntries = [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  const groupDefinitions = new Map(policy.pathGroups.map((group) => [group.id, group]));
  let risk = maxRisk(['R0', ...groupEntries.map(([id]) => groupDefinitions.get(id).riskFloor)]);
  if (controlPathsChanged.length > 0) risk = 'R4';
  const escalated = escalationReasons.length > 0 || policy.riskClasses[risk].fullPlan === true;

  const laneReasons = new Map();
  const addLane = (id, reason) => {
    if (!laneReasons.has(id)) laneReasons.set(id, new Set());
    laneReasons.get(id).add(reason);
  };
  const allLaneIds = Object.keys(policy.lanes).sort();
  if (escalated) {
    const reason = escalationReasons.length > 0 ? `escalated:${escalationReasons.join(',')}` : `risk:${risk}:full-plan`;
    for (const id of allLaneIds) addLane(id, reason);
  } else {
    for (const id of policy.alwaysLanes) addLane(id, 'always');
    for (const [groupId] of groupEntries) for (const id of groupDefinitions.get(groupId).lanes ?? []) addLane(id, `group:${groupId}`);
    for (const id of policy.riskClasses[risk].requiredLanes) addLane(id, `risk:${risk}`);
    if (windowsFull) for (const id of allLaneIds) if (policy.lanes[id].family === 'windows') addLane(id, 'label:windows-full');
  }

  const requestedMode = EXECUTION_MODES.includes(input.executionMode) ? input.executionMode : policy.defaultExecutionMode;
  const hostedForcedReasons = [];
  if (forceHosted) hostedForcedReasons.push('label:force-hosted');
  if (trust !== 'T1') hostedForcedReasons.push(`trust:${trust}`);
  if (risk === 'R4') hostedForcedReasons.push('risk:R4');
  if (escalationReasons.length > 0) hostedForcedReasons.push('escalated');
  const hostedForced = hostedForcedReasons.length > 0;
  const effectiveMode = hostedForced ? 'hosted' : requestedMode;

  const selected = [];
  const skipped = [];
  for (const id of allLaneIds) {
    const lane = policy.lanes[id];
    if (laneReasons.has(id)) {
      const runnerClassId = lane.trustedOnly && effectiveMode === 'hosted' ? lane.hostedFallback : lane.runner;
      const runnerClass = policy.runnerClasses[runnerClassId];
      selected.push({
        lane: id,
        checkName: lane.checkName,
        family: lane.family,
        runnerClass: runnerClassId,
        runner: [...runnerClass.labels],
        hosted: runnerClass.hosted,
        required: true,
        reasons: [...laneReasons.get(id)].sort(),
      });
    } else {
      skipped.push({ lane: id, checkName: lane.checkName, family: lane.family, reason: 'not selected by any changed path group, risk class, or label' });
    }
  }

  return {
    schemaVersion: PLAN_SCHEMA_VERSION,
    policyId: policy.policyId,
    policyDigest: digest,
    mode: policy.mode,
    event: {
      name: input.eventName ?? null,
      repository: input.repository ?? null,
      pullRequest: Number.isInteger(input.pullRequestNumber) ? input.pullRequestNumber : null,
      ref: input.ref ?? null,
      isDraft: input.isDraft === true,
    },
    baseSha: input.baseSha ?? null,
    headSha: input.headSha ?? null,
    mergeSha: input.mergeSha ?? null,
    mergeTreeSha: input.mergeTreeSha ?? null,
    actor: {
      login: input.actorLogin ?? null,
      type: input.actorType ?? null,
      association: input.authorAssociation ?? null,
      isFork: input.isFork === true,
    },
    labels,
    trust,
    risk,
    executionMode: { requested: requestedMode, effective: effectiveMode, hostedForced, reasons: hostedForcedReasons },
    escalated,
    escalationReasons,
    changedFiles: { count: changedFiles.length, paths: changedFiles },
    groups: groupEntries.map(([id, paths]) => ({ id, riskFloor: groupDefinitions.get(id).riskFloor, paths: paths.length })),
    unmappedPaths: uniqueSorted(unmapped),
    controlPathsChanged: uniqueSorted(controlPathsChanged),
    selected,
    skipped,
    plannerError: null,
  };
}

/** The plan a failed planner writes: every lane, hosted, with the error recorded. */
export function errorPlan(input, policy, digest, error) {
  const lanes = policy && policy.lanes && typeof policy.lanes === 'object' ? policy.lanes : {};
  const runnerClasses = policy && policy.runnerClasses ? policy.runnerClasses : {};
  const allLaneIds = Object.keys(lanes).sort();
  return {
    schemaVersion: PLAN_SCHEMA_VERSION,
    policyId: policy && policy.policyId ? policy.policyId : null,
    policyDigest: digest ?? null,
    mode: policy && policy.mode ? policy.mode : 'shadow',
    event: { name: input.eventName ?? null, repository: input.repository ?? null, pullRequest: Number.isInteger(input.pullRequestNumber) ? input.pullRequestNumber : null, ref: input.ref ?? null, isDraft: input.isDraft === true },
    baseSha: input.baseSha ?? null,
    headSha: input.headSha ?? null,
    mergeSha: input.mergeSha ?? null,
    mergeTreeSha: input.mergeTreeSha ?? null,
    actor: { login: input.actorLogin ?? null, type: input.actorType ?? null, association: input.authorAssociation ?? null, isFork: input.isFork === true },
    labels: uniqueSorted((input.labels ?? []).map(String)),
    trust: 'T3',
    risk: 'R4',
    executionMode: { requested: 'hosted', effective: 'hosted', hostedForced: true, reasons: ['planner-error'] },
    escalated: true,
    escalationReasons: ['planner-error'],
    changedFiles: { count: (input.changedFiles ?? []).length, paths: uniqueSorted((input.changedFiles ?? []).map(String)) },
    groups: [],
    unmappedPaths: [],
    controlPathsChanged: [],
    selected: allLaneIds.map((id) => {
      const lane = lanes[id];
      const runnerClassId = lane.hostedFallback && runnerClasses[lane.hostedFallback] ? lane.hostedFallback : lane.runner;
      const runnerClass = runnerClasses[runnerClassId] ?? { labels: ['ubuntu-latest'], hosted: true };
      return { lane: id, checkName: lane.checkName, family: lane.family ?? null, runnerClass: runnerClassId ?? 'hostedLinux', runner: [...runnerClass.labels], hosted: runnerClass.hosted !== false, required: true, reasons: ['escalated:planner-error'] };
    }),
    skipped: [],
    plannerError: { name: error && error.name ? error.name : 'Error', message: String(error && error.message ? error.message : error).slice(0, 2000) },
  };
}

export function validatePlan(plan) {
  const errors = [];
  if (!plan || typeof plan !== 'object') return ['plan must be an object'];
  if (plan.schemaVersion !== PLAN_SCHEMA_VERSION) errors.push(`schemaVersion must be ${PLAN_SCHEMA_VERSION}`);
  if (!isNonEmptyString(plan.policyId)) errors.push('policyId is required');
  if (!/^sha256:[0-9a-f]{64}$/.test(String(plan.policyDigest ?? ''))) errors.push('policyDigest must be sha256:<hex>');
  if (!['shadow', 'enforce'].includes(plan.mode)) errors.push('mode must be shadow or enforce');
  if (!TRUST_CLASSES.includes(plan.trust)) errors.push('trust must be T0..T4');
  if (!RISK_ORDER.includes(plan.risk)) errors.push('risk must be R0..R4');
  if (typeof plan.escalated !== 'boolean') errors.push('escalated must be boolean');
  if (!Array.isArray(plan.escalationReasons)) errors.push('escalationReasons must be an array');
  if (!plan.executionMode || !EXECUTION_MODES.includes(plan.executionMode.effective)) errors.push('executionMode.effective must be hosted|hybrid|self-hosted');
  if (!Array.isArray(plan.selected)) errors.push('selected must be an array');
  else plan.selected.forEach((entry, index) => {
    if (!entry || !isNonEmptyString(entry.lane) || !isNonEmptyString(entry.checkName)) errors.push(`selected[${index}] needs lane and checkName`);
    if (!entry || !isStringArray(entry.runner) || entry.runner.length === 0) errors.push(`selected[${index}].runner must be a non-empty string array`);
    if (!entry || !Array.isArray(entry.reasons) || entry.reasons.length === 0) errors.push(`selected[${index}].reasons must be non-empty`);
  });
  if (!Array.isArray(plan.skipped)) errors.push('skipped must be an array');
  else plan.skipped.forEach((entry, index) => {
    if (!entry || !isNonEmptyString(entry.lane) || !isNonEmptyString(entry.checkName)) errors.push(`skipped[${index}] needs lane and checkName`);
    if (!entry || !isNonEmptyString(entry.reason)) errors.push(`skipped[${index}].reason is required`);
  });
  if (plan.trust !== 'T1' && plan.executionMode && plan.executionMode.effective !== 'hosted') errors.push('a non-T1 plan must execute hosted');
  if (plan.risk === 'R4' && plan.executionMode && plan.executionMode.effective !== 'hosted') errors.push('an R4 plan must execute hosted');
  if (Array.isArray(plan.selected) && plan.executionMode && plan.executionMode.effective === 'hosted' && plan.selected.some((entry) => entry && entry.hosted === false)) errors.push('a hosted plan must not select a self-hosted runner');
  return errors;
}

const PLANNER_FAILURE_CODES = new Set(['plan-missing', 'plan-invalid', 'planner-error', 'plan-job-failed', 'policy-digest-mismatch', 'head-sha-mismatch', 'base-sha-mismatch']);

/**
 * Evaluate the gate.
 * @param {object|null} plan
 * @param {{mode:'shadow'|'enforce', expectedHeadSha?:string, expectedBaseSha?:string, expectedPolicyDigest?:string, planJobResult?:string, results?:Record<string,{conclusion:string, headSha?:string}>|null}} context
 */
export function evaluateGate(plan, context) {
  const mode = context.mode === 'enforce' ? 'enforce' : 'shadow';
  const failures = [];
  const notes = [];
  if (!plan) {
    failures.push({ code: 'plan-missing', detail: 'no plan receipt was produced' });
  } else {
    const planErrors = validatePlan(plan);
    if (planErrors.length > 0) failures.push({ code: 'plan-invalid', detail: planErrors.join('; ') });
    if (plan.plannerError) failures.push({ code: 'planner-error', detail: `${plan.plannerError.name}: ${plan.plannerError.message}` });
    if (context.planJobResult && context.planJobResult !== 'success') failures.push({ code: 'plan-job-failed', detail: `plan job result: ${context.planJobResult}` });
    if (context.expectedHeadSha && plan.headSha !== context.expectedHeadSha) failures.push({ code: 'head-sha-mismatch', detail: `plan ${plan.headSha} vs event ${context.expectedHeadSha}` });
    if (context.expectedBaseSha && plan.baseSha !== context.expectedBaseSha) failures.push({ code: 'base-sha-mismatch', detail: `plan ${plan.baseSha} vs event ${context.expectedBaseSha}` });
    if (context.expectedPolicyDigest && plan.policyDigest !== context.expectedPolicyDigest) failures.push({ code: 'policy-digest-mismatch', detail: `plan ${plan.policyDigest} vs policy ${context.expectedPolicyDigest}` });
    if (context.results && typeof context.results === 'object') {
      for (const entry of plan.selected ?? []) {
        const result = context.results[entry.checkName];
        if (!result) failures.push({ code: 'selected-evidence-missing', detail: entry.checkName });
        else if (result.conclusion !== 'success') failures.push({ code: 'selected-not-success', detail: `${entry.checkName}: ${result.conclusion}` });
        else if (result.headSha && plan.headSha && result.headSha !== plan.headSha) failures.push({ code: 'evidence-wrong-sha', detail: `${entry.checkName}: ${result.headSha}` });
      }
    } else {
      notes.push('no job evidence supplied — shadow phase evaluates the plan receipt only');
    }
    for (const entry of plan.skipped ?? []) if (!entry || !isNonEmptyString(entry.reason)) failures.push({ code: 'skipped-without-reason', detail: entry ? entry.lane : 'unknown' });
    if (plan.escalated) notes.push(`escalated: ${plan.escalationReasons.join(', ') || 'full plan by risk class'}`);
  }
  const plannerFailures = failures.filter((failure) => PLANNER_FAILURE_CODES.has(failure.code));
  const wouldFail = failures.length > 0;
  const ok = mode === 'enforce' ? !wouldFail : plannerFailures.length === 0;
  return { mode, ok, wouldFail, failures, plannerFailures, notes };
}

export function renderPlanSummary(plan) {
  const lines = [];
  lines.push(`### Smart CI plan (${plan.mode})`);
  lines.push('');
  lines.push(`policy \`${plan.policyId}\` · digest \`${String(plan.policyDigest).slice(0, 19)}…\` · event \`${plan.event.name}\`${plan.event.pullRequest ? ` #${plan.event.pullRequest}` : ''}`);
  lines.push(`base \`${plan.baseSha}\` · head \`${plan.headSha}\`${plan.mergeTreeSha ? ` · merge tree \`${plan.mergeTreeSha}\`` : ''}`);
  lines.push(`trust **${plan.trust}** · risk **${plan.risk}** · execution ${plan.executionMode.effective}${plan.executionMode.hostedForced ? ` (hosted forced: ${plan.executionMode.reasons.join(', ')})` : ''} · escalated **${plan.escalated ? 'yes' : 'no'}**${plan.escalationReasons.length ? ` (${plan.escalationReasons.join(', ')})` : ''}`);
  lines.push('');
  if (plan.plannerError) {
    lines.push(`> **Planner error** — ${plan.plannerError.name}: ${plan.plannerError.message}`);
    lines.push('');
  }
  lines.push(`changed files: ${plan.changedFiles.count} · groups: ${plan.groups.map((group) => `${group.id}(${group.riskFloor}×${group.paths})`).join(', ') || 'none'}`);
  if (plan.controlPathsChanged.length) lines.push(`control paths changed: ${plan.controlPathsChanged.map((path) => `\`${path}\``).join(', ')}`);
  if (plan.unmappedPaths.length) lines.push(`unmapped paths (escalate): ${plan.unmappedPaths.map((path) => `\`${path}\``).join(', ')}`);
  lines.push('');
  lines.push('| Lane | Check | Runner | Reasons |');
  lines.push('| --- | --- | --- | --- |');
  for (const entry of plan.selected) lines.push(`| ${entry.lane} | ${entry.checkName} | ${entry.runnerClass} | ${entry.reasons.join(', ')} |`);
  if (plan.skipped.length) {
    lines.push('');
    lines.push(`<details><summary>${plan.skipped.length} lane(s) not selected (shadow: \`ci-required.yml\` still runs them)</summary>`);
    lines.push('');
    for (const entry of plan.skipped) lines.push(`- ${entry.lane} — ${entry.reason}`);
    lines.push('');
    lines.push('</details>');
  }
  lines.push('');
  return `${lines.join('\n')}\n`;
}

export function renderGateSummary(verdict, plan) {
  const lines = [];
  lines.push(`### Smart CI / Required Gate — ${verdict.mode} — ${verdict.ok ? '✅ pass' : '❌ fail'}${verdict.mode === 'shadow' ? ` (would ${verdict.wouldFail ? 'FAIL' : 'pass'} in enforce mode)` : ''}`);
  lines.push('');
  if (plan) lines.push(`policy \`${plan.policyId}\` · head \`${plan.headSha}\` · risk ${plan.risk} · trust ${plan.trust} · ${plan.selected.length} selected / ${plan.skipped.length} not selected`);
  for (const failure of verdict.failures) lines.push(`- ❌ \`${failure.code}\` — ${failure.detail}`);
  for (const note of verdict.notes) lines.push(`- ℹ️ ${note}`);
  lines.push('');
  return `${lines.join('\n')}\n`;
}
