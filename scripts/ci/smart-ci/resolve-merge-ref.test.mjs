import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  AUTH_HEADER_ENV,
  MAX_ATTEMPTS,
  observeBaseTip,
  observeMergeRef,
  resolveMergeRef,
  verifyBaseHistoryAncestor,
  writeMergeRefNote,
} from './resolve-merge-ref.mjs';

const CONTROL_BASE = 'a'.repeat(40);
const EVENT_HEAD = 'b'.repeat(40);
const MERGE_SHA = 'c'.repeat(40);
const TREE_SHA = 'd'.repeat(40);
const ADVANCED_BASE = 'f'.repeat(40);
const NEWLINE = String.fromCharCode(10);

function observation(overrides = {}) {
  return {
    mergeSha: MERGE_SHA,
    mergeBaseSha: CONTROL_BASE,
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
    mergeBaseOutput: join(root, 'merge-base-sha.txt'),
    mergeBaseTipOutput: join(root, 'merge-base-tip-sha.txt'),
    qualificationOutput: join(root, 'merge-ref-qualification.txt'),
  };
}

function assertPublished(fixture, { mergeBaseSha = CONTROL_BASE, mergeBaseTipSha = null } = {}) {
  assert.equal(readFileSync(fixture.mergeOutput, 'utf8'), `${MERGE_SHA}\n`);
  assert.equal(readFileSync(fixture.treeOutput, 'utf8'), `${TREE_SHA}\n`);
  assert.equal(readFileSync(fixture.mergeBaseOutput, 'utf8'), `${mergeBaseSha}\n`);
  assert.equal(readFileSync(fixture.mergeBaseTipOutput, 'utf8'), `${mergeBaseTipSha ?? 'null'}\n`);
  assert.equal(readFileSync(fixture.qualificationOutput, 'utf8'), 'qualified\n');
}

function assertNotPublished(fixture) {
  assert.equal(existsSync(fixture.mergeOutput), false);
  assert.equal(existsSync(fixture.treeOutput), false);
  assert.equal(existsSync(fixture.mergeBaseOutput), false);
  assert.equal(existsSync(fixture.mergeBaseTipOutput), false);
  assert.equal(existsSync(fixture.qualificationOutput), false);
}

function resolverOptions(fixture, overrides = {}) {
  return {
    expectedBase: CONTROL_BASE,
    expectedHead: EVENT_HEAD,
    mergeOutput: fixture.mergeOutput,
    treeOutput: fixture.treeOutput,
    mergeBaseOutput: fixture.mergeBaseOutput,
    mergeBaseTipOutput: fixture.mergeBaseTipOutput,
    qualificationOutput: fixture.qualificationOutput,
    resolveBaseTip: async () => CONTROL_BASE,
    verifyBaseAncestor: async () => false,
    sleep: async () => {},
    ...overrides,
  };
}

test('a stale base observation retries and then publishes one valid identity', async () => {
  const fixture = outputFixture();
  const observations = [
    observation({ mergeBaseSha: 'e'.repeat(40) }),
    observation(),
  ];
  const sleeps = [];

  try {
    const resolved = await resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observations.shift(),
      sleep: async (milliseconds) => sleeps.push(milliseconds),
    });

    assert.deepEqual(resolved, observation({ mergeRefMoved: false, mergeBaseTipSha: null, mergeRefQualification: 'qualified' }));
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
      ...resolverOptions(fixture),
      observe: async () => {
        attempts += 1;
        if (attempts === 1) throw new Error('merge ref unavailable');
        return observation();
      },
      sleep: async () => {},
    });

    assert.deepEqual(resolved, observation({ mergeRefMoved: false, mergeBaseTipSha: null, mergeRefQualification: 'qualified' }));
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
        ...resolverOptions(fixture),
        observe: async () => {
          attempts += 1;
          return observation({ mergeBaseSha: 'e'.repeat(40) });
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
        ...resolverOptions(fixture),
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

test('a merge ref regenerated against the live base branch tip resolves as merge-ref-moved', async () => {
  const fixture = outputFixture();
  const advancedBase = ADVANCED_BASE;
  const logs = [];

  try {
    const resolved = await resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observation({ mergeBaseSha: advancedBase }),
      resolveBaseTip: async () => advancedBase,
      sleep: async () => {},
      log: (message) => logs.push(message),
    });

    assert.equal(resolved.mergeRefMoved, true);
    assert.equal(resolved.mergeBaseSha, advancedBase);
    assert.equal(resolved.mergeBaseTipSha, advancedBase);
    assert.equal(resolved.headSha, EVENT_HEAD);
    assert.equal(readFileSync(fixture.mergeOutput, 'utf8'), `${MERGE_SHA}\n`);
    assert.equal(readFileSync(fixture.treeOutput, 'utf8'), `${TREE_SHA}\n`);
    assert.equal(readFileSync(fixture.mergeBaseOutput, 'utf8'), `${ADVANCED_BASE}\n`);
    assert.equal(readFileSync(fixture.mergeBaseTipOutput, 'utf8'), `${ADVANCED_BASE}\n`);
    assert.equal(logs.length, 1);
    assert.match(logs[0], /^merge-ref-moved: named base tip /);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a first parent that is not the live base branch tip stays fail-closed', async () => {
  const fixture = outputFixture();
  const attempts = [];

  try {
    await assert.rejects(resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => { attempts.push('observe'); return observation({ mergeBaseSha: 'e'.repeat(40) }); },
      resolveBaseTip: async () => 'f'.repeat(40),
      sleep: async () => {},
    }), /not proven in named base history/);

    assert.equal(attempts.length, MAX_ATTEMPTS);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a retained control-base merge ref is unqualified after the named base advances', async () => {
  const fixture = outputFixture();
  const retainedBase = CONTROL_BASE;
  const ancestryChecks = [];
  let attempts = 0;

  try {
    const resolved = await resolveMergeRef({
      ...resolverOptions(fixture),
      qualificationOutput: fixture.qualificationOutput,
      observe: async () => {
        attempts += 1;
        return observation({ mergeBaseSha: retainedBase });
      },
      resolveBaseTip: async () => ADVANCED_BASE,
      verifyBaseAncestor: async (ancestor, descendant) => {
        ancestryChecks.push([ancestor, descendant]);
        return true;
      },
      sleep: async () => {},
    });

    assert.equal(attempts, MAX_ATTEMPTS);
    assert.deepEqual(ancestryChecks, [[retainedBase, ADVANCED_BASE]]);
    assert.equal(resolved.mergeRefQualification, 'stale-base-unqualified');
    assert.equal(resolved.mergeBaseSha, retainedBase);
    assert.equal(resolved.mergeBaseTipSha, ADVANCED_BASE);
    assert.equal(existsSync(fixture.mergeOutput), false);
    assert.equal(existsSync(fixture.treeOutput), false);
    assert.equal(existsSync(fixture.mergeBaseOutput), false);
    assert.equal(existsSync(fixture.mergeBaseTipOutput), false);
    assert.equal(readFileSync(fixture.qualificationOutput, 'utf8'), 'stale-base-unqualified\n');
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('a foreign retained first parent remains a hard failure even when the event head matches', async () => {
  const fixture = outputFixture();
  let ancestryChecks = 0;

  try {
    await assert.rejects(resolveMergeRef({
      ...resolverOptions(fixture),
      qualificationOutput: fixture.qualificationOutput,
      observe: async () => observation({ mergeBaseSha: 'e'.repeat(40) }),
      resolveBaseTip: async () => ADVANCED_BASE,
      verifyBaseAncestor: async () => { ancestryChecks += 1; return false; },
      sleep: async () => {},
    }), /not proven in named base history/);

    assert.equal(ancestryChecks, 1);
    assertNotPublished(fixture);
    assert.equal(existsSync(fixture.qualificationOutput), false);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('an unreadable ancestry proof fails closed and removes every output', async () => {
  const fixture = outputFixture();
  try {
    for (const path of [fixture.mergeOutput, fixture.treeOutput, fixture.mergeBaseOutput, fixture.mergeBaseTipOutput, fixture.qualificationOutput]) {
      writeFileSync(path, 'stale\n');
    }
    await assert.rejects(resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observation(),
      resolveBaseTip: async () => ADVANCED_BASE,
      verifyBaseAncestor: async () => { throw new Error('compare unavailable'); },
    }), /base history could not be verified/);
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
      ...resolverOptions(fixture),
      observe: async () => observation({ mergeBaseSha: 'f'.repeat(40), headSha: '9'.repeat(40) }),
      resolveBaseTip: async () => { baseTipReads += 1; return 'f'.repeat(40); },
      sleep: async () => {},
    }), /head mismatch/);

    assert.equal(baseTipReads, 0);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('an unreadable base branch tip stays fail-closed', async () => {
  const fixture = outputFixture();

  try {
    await assert.rejects(resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observation({ mergeBaseSha: 'f'.repeat(40) }),
      resolveBaseTip: async () => { throw new Error('network down'); },
      sleep: async () => {},
    }), /the base branch tip could not be read/);

    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('observeBaseTip fetches exactly the named base ref and keeps the token out of argv', async () => {
  const token = 'test-token-that-must-not-enter-argv';
  const calls = [];
  const executeGit = async (args, options) => {
    calls.push({ args, options });
    if (args.includes('fetch')) return '';
    return `${ADVANCED_BASE}${NEWLINE}`;
  };

  const tip = await observeBaseTip({ baseRef: 'release/v0.3', token, executeGit });

  assert.equal(tip, ADVANCED_BASE);
  assert.equal(calls.length, 2);
  assert.match(calls[0].args[0], /^--config-env=http\.extraHeader=/);
  assert.deepEqual(calls[0].args.slice(1), ['fetch', '--no-tags', '--depth=1', 'origin', 'refs/heads/release/v0.3']);
  assert.deepEqual(calls[1].args, ['rev-parse', 'FETCH_HEAD^{commit}']);
  assert.equal(JSON.stringify(calls.map((call) => call.args)).includes(token), false);
  // The token travels only in the fetch environment, never the argument list.
  assert.ok(String(calls[0].options.env[AUTH_HEADER_ENV]).includes('AUTHORIZATION: basic '));
  assert.equal(calls[1].options.env[AUTH_HEADER_ENV], undefined);
});

test('observeBaseTip rejects a missing base ref and a non-SHA answer', async () => {
  await assert.rejects(
    observeBaseTip({ baseRef: '', token: 'test-token', executeGit: async () => '' }),
    /base ref is required/,
  );
  await assert.rejects(
    observeBaseTip({ baseRef: 'main', token: 'test-token', executeGit: async () => `not-a-sha${NEWLINE}` }),
    /did not yield one commit SHA/,
  );
});

test('the GitHub compare proof binds exact validated SHAs and keeps the token in headers', async () => {
  const token = 'compare-token-that-must-not-enter-the-url';
  const calls = [];
  const result = await verifyBaseHistoryAncestor({
    repository: 'Chris0Jeky/Taskdeck',
    ancestorSha: CONTROL_BASE,
    descendantSha: ADVANCED_BASE,
    token,
    request: async (url, options) => {
      calls.push({ url, options });
      return {
        ok: true,
        status: 200,
        json: async () => ({
          status: 'ahead',
          ahead_by: 3,
          behind_by: 0,
          base_commit: { sha: CONTROL_BASE },
          merge_base_commit: { sha: CONTROL_BASE },
        }),
      };
    },
  });

  assert.equal(result, true);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, `https://api.github.com/repos/Chris0Jeky/Taskdeck/compare/${CONTROL_BASE}...${ADVANCED_BASE}`);
  assert.equal(calls[0].url.includes(token), false);
  assert.equal(calls[0].options.method, 'GET');
  assert.equal(calls[0].options.redirect, 'error');
  assert.equal(calls[0].options.headers.Authorization, `Bearer ${token}`);
});

test('the GitHub compare proof rejects malformed repositories, non-ancestry, and unreadable responses', async () => {
  await assert.rejects(
    verifyBaseHistoryAncestor({
      repository: '../Taskdeck',
      ancestorSha: CONTROL_BASE,
      descendantSha: ADVANCED_BASE,
      token: 'test-token',
      request: async () => { throw new Error('must not run'); },
    }),
    /ordinary owner and repository names/,
  );

  const notAncestor = await verifyBaseHistoryAncestor({
    repository: 'Chris0Jeky/Taskdeck',
    ancestorSha: CONTROL_BASE,
    descendantSha: ADVANCED_BASE,
    token: 'test-token',
    request: async () => ({
      ok: true,
      status: 200,
      json: async () => ({
        status: 'diverged',
        ahead_by: 2,
        behind_by: 1,
        base_commit: { sha: CONTROL_BASE },
        merge_base_commit: { sha: '9'.repeat(40) },
      }),
    }),
  });
  assert.equal(notAncestor, false);

  await assert.rejects(
    verifyBaseHistoryAncestor({
      repository: 'Chris0Jeky/Taskdeck',
      ancestorSha: CONTROL_BASE,
      descendantSha: ADVANCED_BASE,
      token: 'test-token',
      request: async () => ({ ok: false, status: 503 }),
    }),
    /status 503/,
  );
});

test('the CLI note wiring records an accepted moved base with an LF-terminated line', async () => {
  const fixture = outputFixture();
  const notePath = join(fixture.root, 'notes', 'merge-ref-note.txt');

  try {
    const resolved = await resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observation({ mergeBaseSha: ADVANCED_BASE }),
      resolveBaseTip: async () => ADVANCED_BASE,
      sleep: async () => {},
    });

    assert.equal(resolved.mergeRefMoved, true);
    writeMergeRefNote(notePath, CONTROL_BASE, resolved);

    const note = readFileSync(notePath, 'utf8');
    assert.match(note, /^merge-ref-moved: named base tip /);
    assert.ok(note.includes(CONTROL_BASE));
    assert.ok(note.includes(ADVANCED_BASE));
    assert.equal(note.endsWith(NEWLINE), true);
    assert.equal(note.includes('\r'), false);
    assertPublished(fixture, { mergeBaseSha: ADVANCED_BASE, mergeBaseTipSha: ADVANCED_BASE });
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});

test('failure removes stale outputs for every merge identity field', async () => {
  const fixture = outputFixture();
  try {
    for (const path of [fixture.mergeOutput, fixture.treeOutput, fixture.mergeBaseOutput, fixture.mergeBaseTipOutput, fixture.qualificationOutput]) {
      writeFileSync(path, 'stale\n');
    }
    await assert.rejects(resolveMergeRef({
      ...resolverOptions(fixture),
      observe: async () => observation({ mergeBaseSha: ADVANCED_BASE }),
      resolveBaseTip: async () => 'e'.repeat(40),
      sleep: async () => {},
    }), /not proven in named base history/);
    assertNotPublished(fixture);
  } finally {
    rmSync(fixture.root, { recursive: true, force: true });
  }
});
