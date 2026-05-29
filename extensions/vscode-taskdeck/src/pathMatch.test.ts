import { isPathWithin } from './pathMatch';

// Simple test runner (no test framework dependency for the extension)
let passed = 0;
let failed = 0;

function assert(condition: boolean, label: string): void {
  if (condition) {
    passed++;
    console.log(`  PASS: ${label}`);
  } else {
    failed++;
    console.error(`  FAIL: ${label}`);
  }
}

console.log('isPathWithin tests:');

assert(isPathWithin('/repo', '/repo'), 'equal paths match');
assert(isPathWithin('/repo/src/main.ts', '/repo'), 'document in subfolder matches repo root');
assert(isPathWithin('/repo/a/b/c.ts', '/repo/a'), 'deeply nested document matches ancestor');
assert(isPathWithin('/repo/src/main.ts', '/repo/'), 'trailing slash on parent is tolerated');
assert(!isPathWithin('/repo-other/main.ts', '/repo'), 'sibling prefix is not a containment match');
assert(!isPathWithin('/other/main.ts', '/repo'), 'unrelated path does not match');
assert(!isPathWithin('/repo', '/repo/src'), 'ancestor is not within its descendant');

console.log(`\nResults: ${passed} passed, ${failed} failed`);
if (failed > 0) {
  process.exit(1);
}
