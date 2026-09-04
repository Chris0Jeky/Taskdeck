import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  MAX_ATTEMPTS,
  observeMergeRef,
  resolveMergeRef,
} from './resolve-merge-ref.mjs';

const CONTROL_BASE = 'a'.repeat(40);
const EVENT_HEAD = 'b'.repeat(40);
const MERGE_SHA = 'c'.repeat(40);
const TREE_SHA = 'd'.repeat(40);

function observation(overrides = {}) {
  return {
    mergeSha: MERGE_SHA,
    baseSha: CONTROL_BASE,
    headSha: EVENT_HEAD,
    treeSha: TREE_SHA,
    ...overrides,
  };
}

function outputFixture() {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-smart-ci-resolver-'));
  return {
    root,
    mergeOutput: join(root, 'merge-sha.txt'),
    treeOutput: join(root, 'merge-tree-sha.txt'),
  };
}

function assertPublished(fixture) {
  assert.equal(readFileSync(fixture.mergeOutput, 'utf8'), `${MERGE_SHA}\n`);
  assert.equal(readFileSync(fixture.treeOutput, 'utf8'), `${TREE_SHA}\n`);
}

function assertNotPublished(fixture) {
  assert.equal(existsSync(fixture.mergeOutput), false);
  assert.equal(existsSync(fixture.treeOutput), false);
}

test('a stale base observation retries and then publishes one valid identity', async () => {
  const fixture = outputFixture();
  const observations = [
    observation({ baseSha: 'e'.repeat(40) }),
    observation(),
  ];
  const sleeps = [];

  try {
    const resolved = await resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => observations.shift(),
      sleep: async (milliseconds) => sleeps.push(milliseconds),
    });

    assert.deepEqual(resolved, observation({ mergeRefMoved: false, baseTipSha: null }));
    assert.equal(observations.length, 0);
    assert.equal(sleeps.length, 1);
    assertPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('an unavailable observation retries and then publishes one valid identity', async () => {
  const fixture = outputFixture();
  let attempts = 0;

  try {
    const resolved = await resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => {
        attempts += 1;
        if (attempts === 1) throw new Error('merge ref unavailable');
        return observation();
      },
      sleep: async () => {},
    });

    assert.deepEqual(resolved, observation({ mergeRefMoved: false, baseTipSha: null }));
    assert.equal(attempts, 2);
    assertPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a persistently wrong base stops after three attempts without outputs', async () => {
  const fixture = outputFixture();
  let attempts = 0;

  try {
    await assert.rejects(
      resolveMergeRef({
        expectedBase: CONTROL_BASE,
        expectedHead: EVENT_HEAD,
        mergeOutput: fixture.mergeOutput,
        treeOutput: fixture.treeOutput,
        observe: async () => {
          attempts += 1;
          return observation({ baseSha: 'e'.repeat(40) });
        },
        sleep: async () => {},
      }),
      /failed closed after 3 attempts: base mismatch/,
    );

    assert.equal(attempts, MAX_ATTEMPTS);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a persistently wrong head stops without publishing outputs', async () => {
  const fixture = outputFixture();
  let attempts = 0;

  try {
    await assert.rejects(
      resolveMergeRef({
        expectedBase: CONTROL_BASE,
        expectedHead: EVENT_HEAD,
        mergeOutput: fixture.mergeOutput,
        treeOutput: fixture.treeOutput,
        observe: async () => {
          attempts += 1;
          return observation({ headSha: 'f'.repeat(40) });
        },
        sleep: async () => {},
      }),
      /failed closed after 3 attempts: head mismatch/,
    );

    assert.equal(attempts, MAX_ATTEMPTS);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('one rev-parse invocation returns a coherent four-value observation', async () => {
  const token = 'test-token-that-must-not-enter-argv';
  const calls = [];
  const executeGit = async (args, options) => {
    calls.push({ args, options });
    if (args.includes('fetch')) return '';
    return `${MERGE_SHA}\n${CONTROL_BASE}\n${EVENT_HEAD}\n${TREE_SHA}\n`;
  };

  const resolved = await observeMergeRef({
    pullRequestNumber: 2401,
    token,
    executeGit,
  });

  assert.deepEqual(resolved, observation());
  assert.equal(calls.length, 2);
  assert.deepEqual(calls[1].args, [
    'rev-parse',
    'FETCH_HEAD^{commit}',
    'FETCH_HEAD^1',
    'FETCH_HEAD^2',
    'FETCH_HEAD^{tree}',
  ]);
  assert.equal(JSON.stringify(calls.map((call) => call.args)).includes(token), false);
  assert.match(calls[0].args[0], /^--config-env=http\.extraHeader=/);
});

test('a merge ref regenerated against the live protected base tip resolves as merge-ref-moved', async () => {
  const fixture = outputFixture();
  const advancedBase = 'f'.repeat(40);
  const logs = [];

  try {
    const resolved = await resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => observation({ baseSha: advancedBase }),
      resolveBaseTip: async () => advancedBase,
      sleep: async () => {},
      log: (message) => logs.push(message),
    });

    assert.equal(resolved.mergeRefMoved, true);
    assert.equal(resolved.baseTipSha, advancedBase);
    assert.equal(resolved.headSha, EVENT_HEAD);
    assertPublished(fixture);
    assert.equal(logs.length, 1);
    assert.match(logs[0], /^merge-ref-moved —/);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a first parent that is not the live protected base tip stays fail-closed', async () => {
  const fixture = outputFixture();
  const attempts = [];

  try {
    await assert.rejects(resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => { attempts.push('observe'); return observation({ baseSha: 'e'.repeat(40) }); },
      resolveBaseTip: async () => 'f'.repeat(40),
      sleep: async () => {},
    }), /not the live protected base tip/);

    assert.equal(attempts.length, MAX_ATTEMPTS);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a moved base never excuses a head mismatch', async () => {
  const fixture = outputFixture();
  let baseTipReads = 0;

  try {
    await assert.rejects(resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => observation({ baseSha: 'f'.repeat(40), headSha: '9'.repeat(40) }),
      resolveBaseTip: async () => { baseTipReads += 1; return 'f'.repeat(40); },
      sleep: async () => {},
    }), /base and head mismatch/);

    assert.equal(baseTipReads, 0);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('an unreadable protected base tip stays fail-closed', async () => {
  const fixture = outputFixture();

  try {
    await assert.rejects(resolveMergeRef({
      expectedBase: CONTROL_BASE,
      expectedHead: EVENT_HEAD,
      mergeOutput: fixture.mergeOutput,
      treeOutput: fixture.treeOutput,
      observe: async () => observation({ baseSha: 'f'.repeat(40) }),
      resolveBaseTip: async () => { throw new Error('network down'); },
      sleep: async () => {},
    }), /the protected base tip could not be read/);

    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});
