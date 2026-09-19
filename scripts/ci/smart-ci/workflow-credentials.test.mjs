import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { test } from 'node:test';
import { inventoryActionPins } from './action-pins.mjs';

const directory = new URL('../../../.github/workflows/', import.meta.url);
const workflows = readdirSync(directory).filter((name) => /\.ya?ml$/.test(name))
  .map((path) => ({ path, text: readFileSync(new URL(path, directory), 'utf8') }));

// These are the reviewed API/artifact publishers. None needs Git credentials
// persisted by checkout. New write grants require an explicit purpose review.
const writeGrants = [
  'pages-frontend.yml/deploy/id-token', // Authenticate the Pages deployment.
  'pages-frontend.yml/deploy/pages', // Publish the already built Pages artifact.
  'release-container.yml/publish-image/packages', // Push the GHCR image.
  'release-desktop.yml/compose-notes/contents', // Generate release notes via API.
  'release-desktop.yml/create-release/contents', // Create/upload/edit the release via API.
].sort();

const indent = (line) => line.length - line.trimStart().length;
const linesOf = (text) => text.split(/\r?\n/);

function childLines(lines, index) {
  const depth = indent(lines[index]);
  const result = [];
  for (let next = index + 1; next < lines.length; next += 1) {
    if (!lines[next].trim() || /^\s*#/.test(lines[next])) continue;
    if (indent(lines[next]) <= depth) break;
    result.push(lines[next]);
  }
  return result;
}

function assertCheckoutCredentials(files) {
  const checkouts = inventoryActionPins(files).entries.filter((entry) => entry.action === 'actions/checkout');
  assert.ok(checkouts.length > 0, 'workflow inventory must include checkout');
  for (const entry of checkouts) {
    const lines = linesOf(files.find((file) => file.path === entry.file).text);
    const index = entry.line - 1;
    // Deliberately checks the repository's block-mapping step shape, not general YAML.
    assert.match(lines[index], /^        uses:/, `${entry.file}:${entry.line}: unsupported checkout shape`);
    let start = index;
    while (start >= 0 && !/^      - /.test(lines[start])) start -= 1;
    assert.ok(start >= 0, `${entry.file}:${entry.line}: checkout must belong to a step`);
    const step = childLines(lines, start);
    const withIndex = step.findIndex((line) => /^        with:\s*$/.test(line));
    assert.ok(withIndex >= 0, `${entry.file}:${entry.line}: checkout must disable persisted credentials`);
    const inputs = childLines(step, withIndex);
    assert.ok(inputs.some((line) => /^          persist-credentials: false\s*(?:#.*)?$/.test(line)),
      `${entry.file}:${entry.line}: checkout must disable persisted credentials`);
  }
}

function collectWriteGrants(files) {
  const grants = [];
  for (const file of files) {
    const lines = linesOf(file.text);
    let job = 'workflow';
    let inJobs = false;
    lines.forEach((line, index) => {
      if (line === 'jobs:') inJobs = true;
      const jobMatch = inJobs && line.match(/^  ([\w-]+):\s*$/);
      if (jobMatch) job = jobMatch[1];
      if (!/^\s*permissions:/.test(line)) return;
      assert.match(line, /^(?: {4})?permissions:\s*(?:\{\})?\s*$/, `${file.path}: unsupported permissions mapping`);
      for (const permission of childLines(lines, index)) {
        const match = permission.trim().match(/^([\w-]+): (read|write|none)\s*(?:#.*)?$/);
        assert.ok(match, `${file.path}: unsupported permission grant`);
        if (match[2] === 'write') grants.push(`${file.path}/${indent(line) === 0 ? 'workflow' : job}/${match[1]}`);
      }
    });
  }
  return grants.sort();
}

test('all workflow checkouts disable credential persistence', () => assertCheckoutCredentials(workflows));

test('write permissions are restricted to the reviewed publishing jobs', () => {
  assert.deepEqual(collectWriteGrants(workflows), writeGrants);
});

test('a checkout cannot borrow another step or env value to satisfy its credential input', () => {
  const text = 'jobs:\n  build:\n    steps:\n      - name: Checkout\n        uses: actions/checkout@abc\n        env:\n          persist-credentials: false\n      - name: Other\n        with:\n          persist-credentials: false\n';
  assert.throws(() => assertCheckoutCredentials([{ path: 'fixture.yml', text }]), /must disable persisted credentials/);
});

test('write-all cannot bypass the explicit publishing grants', () => {
  assert.throws(() => collectWriteGrants([{ path: 'fixture.yml', text: 'permissions: write-all\n' }]), /unsupported permissions mapping/);
});
