import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'

const skillPath = new URL('../.claude/skills/pre-merge-gate/SKILL.md', import.meta.url)
const skill = readFileSync(skillPath, 'utf8')

test('the report cannot claim a clean secrets scan unconditionally', () => {
  assert.doesNotMatch(
    skill,
    /^- \[ \] Secrets scan: CLEAN\s*$/m,
    'the evidence template must render a derived verdict rather than hard-code CLEAN',
  )
  assert.match(skill, /Secrets scan: \$secrets_scan_verdict/)
})

test('secrets evidence is bound to the exact PR head and named hosted check', () => {
  assert.match(skill, /commits\/\$pr_head_oid\/check-runs/)
  assert.match(skill, /filter=latest/)
  assert.match(skill, /Gitleaks Scan/)
  assert.match(skill, /head_sha/)
})

test('only one completed successful exact-head check can produce CLEAN', () => {
  assert.match(skill, /MISSING/)
  assert.match(skill, /AMBIGUOUS/)
  assert.match(skill, /completed/)
  assert.match(skill, /success/)
  assert.match(skill, /NOT VERIFIED/)
  assert.match(skill, /secrets_scan_verdict="CLEAN"/)
})
