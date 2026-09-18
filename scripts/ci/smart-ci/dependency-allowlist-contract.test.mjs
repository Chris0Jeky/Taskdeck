import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const EXPECTED_ADVISORY_PATTERN =
  '^(?:GHSA-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}|CVE-[0-9]{4}-[0-9]{4,}|NPM-[0-9]+|https://\\S+)$';

const schema = JSON.parse(
  readFileSync(
    new URL('../../../ci/schemas/dependency-allowlist.v1.schema.json', import.meta.url),
    'utf8',
  ),
);
const allowlist = JSON.parse(
  readFileSync(new URL('../../../ci/dependency-allowlist.v1.json', import.meta.url), 'utf8'),
);

test('dependency allowlist schema restricts entries to supported advisory identities', () => {
  assert.equal(
    schema.properties.entries.items.properties.advisoryId.pattern,
    EXPECTED_ADVISORY_PATTERN,
  );
});

test('the authoritative dependency allowlist uses unique supported advisory identities', () => {
  assert.equal(allowlist.schemaVersion, 1);
  assert.ok(Array.isArray(allowlist.entries));

  const advisoryPattern = new RegExp(EXPECTED_ADVISORY_PATTERN, 'i');
  const seen = new Set();

  for (const entry of allowlist.entries) {
    assert.match(entry.advisoryId, advisoryPattern);

    const normalizedId = entry.advisoryId.toUpperCase();
    assert.equal(seen.has(normalizedId), false, `duplicate advisory ID: ${normalizedId}`);
    seen.add(normalizedId);
  }
});
