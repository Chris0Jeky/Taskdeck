from pathlib import Path

path = Path('.github/workflows/apply-1543-secrets-governance.yml')
source = path.read_text(encoding='utf-8')
dollar = '$'

replacements = [
    (
        '  group: apply-1543-secrets-governance\n',
        f'  group: apply-1543-secrets-governance-{dollar}{{{{ github.ref }}}}\n',
    ),
    (
        '      - name: Checkout branch\n',
        '      - name: Checkout exact triggering commit\n',
    ),
    (
        '          ref: test/1543-secrets-evidence-verdict\n',
        f'          ref: {dollar}{{{{ github.sha }}}}\n',
    ),
]
for old, new in replacements:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'Expected exactly one applicator anchor {old!r}, found {count}.')
    source = source.replace(old, new, 1)

checkout_anchor = '''          fetch-depth: 0

      - name: Patch required docs governance contract
'''
checkout_replacement = '''          fetch-depth: 0

      - name: Verify exact checkout provenance
        shell: bash
        run: test "$(git rev-parse HEAD)" = "$GITHUB_SHA"

      - name: Patch required docs governance contract
'''
if source.count(checkout_anchor) != 1:
    raise SystemExit('Expected one post-checkout applicator anchor.')
source = source.replace(checkout_anchor, checkout_replacement, 1)

publish_anchor = '''      - name: Commit implementation
        shell: bash
        run: |
          set -euo pipefail
          git config user.name 'github-actions[bot]'
'''
publish_replacement = '''      - name: Commit implementation
        shell: bash
        run: |
          set -euo pipefail
          test "$(git rev-parse HEAD)" = "$GITHUB_SHA"
          git config user.name 'github-actions[bot]'
'''
if source.count(publish_anchor) != 1:
    raise SystemExit('Expected one pre-publication applicator anchor.')
source = source.replace(publish_anchor, publish_replacement, 1)

path.write_text(source, encoding='utf-8', newline='\n')
