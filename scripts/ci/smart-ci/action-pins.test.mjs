import assert from 'node:assert/strict';
import { test } from 'node:test';
import { inventoryActionPins, renderPinsMarkdown } from './action-pins.mjs';

const workflow = `
jobs:
  a:
    steps:
      - uses: actions/checkout@v7
      - name: pinned
        uses: gitleaks/gitleaks-action@e0c47f4f8be36e29cdc102c57e68cb5cbf0e8d1e # v2.3.9
      - uses: ./.github/actions/local-thing
      - uses: docker://alpine:3.20
      - uses: "actions/setup-node@v7"
  b:
    uses: ./.github/workflows/reusable-docs-governance.yml
`;

test('inventoryActionPins classifies local, docker and external references and detects SHA pins', () => {
  const inventory = inventoryActionPins([{ path: '.github/workflows/x.yml', text: workflow }]);
  assert.equal(inventory.summary.references, 6);
  assert.equal(inventory.summary.external, 4);
  assert.equal(inventory.summary.pinned, 1);
  assert.equal(inventory.summary.unpinned, 3);
  const gitleaks = inventory.entries.find((entry) => entry.action === 'gitleaks/gitleaks-action');
  assert.equal(gitleaks.pinned, true);
  assert.equal(gitleaks.comment, 'v2.3.9');
  assert.equal(inventory.entries.find((entry) => entry.uses.startsWith('docker://')).kind, 'docker');
  assert.equal(inventory.entries.filter((entry) => entry.kind === 'local').length, 2);
  assert.deepEqual(inventory.byAction['actions/checkout'].refs, ['v7']);
  assert.equal(inventory.summary.distinctExternalActions, 4);
});

test('renderPinsMarkdown renders the summary line and a row per action', () => {
  const markdown = renderPinsMarkdown(inventoryActionPins([{ path: 'w.yml', text: workflow }]));
  assert.match(markdown, /4 external references/);
  assert.match(markdown, /\| actions\/checkout \| 1 \| 0 \| `v7` \|/);
});
