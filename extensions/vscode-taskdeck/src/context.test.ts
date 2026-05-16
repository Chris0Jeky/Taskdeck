// Test the pure buildCaptureText function without importing vscode-dependent modules.
// We re-implement the type and function inline to avoid the vscode import chain.

interface WorkspaceContext {
  relativePath: string | null;
  language: string;
  lineRange: string | null;
  gitRemoteHash: string | null;
}

function buildCaptureText(selectedText: string | null, context: WorkspaceContext): string {
  const parts: string[] = [];

  if (context.relativePath) {
    const location = context.lineRange
      ? `${context.relativePath}:${context.lineRange}`
      : context.relativePath;
    parts.push(`[${context.language}] ${location}`);
  }

  if (selectedText) {
    parts.push(selectedText);
  }

  if (context.gitRemoteHash) {
    parts.push(`workspace: ${context.gitRemoteHash}`);
  }

  return parts.join('\n\n');
}

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

console.log(`\nResults: ${passed} passed, ${failed} failed`);
if (failed > 0) process.exit(1);
