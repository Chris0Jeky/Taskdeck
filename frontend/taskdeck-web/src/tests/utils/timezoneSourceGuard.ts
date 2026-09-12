import ts from 'typescript'

export interface TimezoneStub {
  line: number
  testName: string | null
  receiver: string
  zone: string | null
}

function literal(node: ts.Node | undefined): string | null {
  return node && (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))
    ? node.text : null
}

/**
 * Find literal stubEnv('TZ', ...) calls without mistaking comments, prose or fixture
 * strings for executed code. This is a convention guard, not data-flow analysis:
 * dynamically computed keys, renamed functions and process.env writes are outside it.
 */
export function findTimezoneStubs(source: string, fileName = 'test.ts'): TimezoneStub[] {
  const kind = fileName.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  const file = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, kind)
  const matches: TimezoneStub[] = []
  function visit(node: ts.Node, testName: string | null = null) {
    if (ts.isCallExpression(node)) {
      const callee = node.expression
      // Only the literal direct it/test form owns the intentional self-test exception.
      if (ts.isIdentifier(callee) && (callee.text === 'it' || callee.text === 'test')) {
        testName = literal(node.arguments[0])
      }
      const property = ts.isPropertyAccessExpression(callee) ? callee.name.text
        : ts.isElementAccessExpression(callee) ? literal(callee.argumentExpression)
          : ts.isIdentifier(callee) ? callee.text : null
      if (property === 'stubEnv' && literal(node.arguments[0]) === 'TZ') {
        matches.push({
          line: file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1,
          testName,
          receiver: ts.isPropertyAccessExpression(callee) || ts.isElementAccessExpression(callee)
            ? callee.expression.getText(file) : '',
          zone: literal(node.arguments[1]),
        })
      }
    }
    ts.forEachChild(node, child => visit(child, testName))
  }
  visit(file)
  return matches
}

export function isTimezoneHelperSelfTest(path: string, match: TimezoneStub): boolean {
  return path === '../utils/timeZone.spec.ts'
    && match.testName === 'is unaffected by a TZ env stub, in any pool'
    && match.receiver === 'vi'
    && match.zone === 'Pacific/Midway'
}
