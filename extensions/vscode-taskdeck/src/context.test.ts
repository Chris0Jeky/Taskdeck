import { buildCaptureText, getSafeDocumentLabel, type WorkspaceContext } from './contextFormatter';

function makeContext(overrides: Partial<WorkspaceContext> = {}): WorkspaceContext {
  return {
    relativePath: 'src/main.ts',
    language: 'typescript',
    lineRange: null,
    gitRemoteHash: 'abc123def456',
    ...overrides,
  };
}

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

console.log('buildCaptureText tests:');

// Test: includes file path and language
{
  const ctx = makeContext();
  const result = buildCaptureText('const x = 1', ctx);
  assert(result.includes('[typescript] src/main.ts'), 'includes file path with language');
  assert(result.includes('const x = 1'), 'includes selected text');
  assert(result.includes('workspace: abc123def456'), 'includes git remote hash');
}

// Test: includes line range when present
{
  const ctx = makeContext({ lineRange: 'L10-L25' });
  const result = buildCaptureText('function foo() {}', ctx);
  assert(result.includes('src/main.ts:L10-L25'), 'includes line range');
}

// Test: works without selection (file context only)
{
  const ctx = makeContext();
  const result = buildCaptureText(null, ctx);
  assert(result.includes('[typescript] src/main.ts'), 'includes file context');
  assert(!result.includes('null'), 'does not include null');
}

// Test: works without git remote
{
  const ctx = makeContext({ gitRemoteHash: null });
  const result = buildCaptureText('code', ctx);
  assert(!result.includes('workspace:'), 'omits workspace when no git remote');
}

// Test: works without file path
{
  const ctx = makeContext({ relativePath: null });
  const result = buildCaptureText('orphan text', ctx);
  assert(!result.includes('[typescript]'), 'omits language prefix when no path');
  assert(result.includes('orphan text'), 'still includes selected text');
}

// Test: separates sections with double newlines
{
  const ctx = makeContext();
  const result = buildCaptureText('selected', ctx);
  const sections = result.split('\n\n');
  assert(sections.length === 3, 'three sections separated by double newlines');
}

// Test: avoids absolute local paths outside a workspace
{
  const result = getSafeDocumentLabel({
    workspaceRelativePath: null,
    isUntitled: false,
    fileName: 'C:\\Users\\alice\\Documents\\private-plan.ts',
    uriScheme: 'file',
    uriFsPath: 'C:\\Users\\alice\\Documents\\private-plan.ts',
  });
  assert(result === 'private-plan.ts', 'uses only basename for files outside a workspace');
  assert(!String(result).includes('Users'), 'omits local directory names');
}

// Test: keeps workspace-relative labels when a workspace is available
{
  const result = getSafeDocumentLabel({
    workspaceRelativePath: 'src/features/capture.ts',
    isUntitled: false,
    fileName: 'C:\\Users\\alice\\repo\\src\\features\\capture.ts',
    uriScheme: 'file',
    uriFsPath: 'C:\\Users\\alice\\repo\\src\\features\\capture.ts',
  });
  assert(result === 'src/features/capture.ts', 'keeps workspace-relative labels');
}

console.log(`\nResults: ${passed} passed, ${failed} failed`);
if (failed > 0) process.exit(1);
