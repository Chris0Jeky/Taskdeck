import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../..', import.meta.url))
const workflowsDirectory = join(repoRoot, '.github', 'workflows')
const workflowPrefix = '.github/workflows/'

function stripYamlComment(sourceLine) {
  let quote = null

  for (let index = 0; index < sourceLine.length; index += 1) {
    const character = sourceLine[index]

    if (quote === '"') {
      if (character === '\\') index += 1
      else if (character === '"') quote = null
      continue
    }
    if (quote === "'") {
      if (character === "'" && sourceLine[index + 1] === "'") index += 1
      else if (character === "'") quote = null
      continue
    }
    if (character === '"' || character === "'") {
      quote = character
      continue
    }
    if (character === '#' && (index === 0 || /\s/.test(sourceLine[index - 1]))) {
      return sourceLine.slice(0, index).trimEnd()
    }
  }

  return sourceLine
}

function workflowSourcesFromDisk() {
  const sources = new Map()

  for (const entry of readdirSync(workflowsDirectory, { withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith('.yml')) {
      sources.set(`${workflowPrefix}${entry.name}`, readFileSync(join(workflowsDirectory, entry.name), 'utf8'))
    }
  }

  return sources
}

function annotateYamlLines(source) {
  const records = []
  let blockScalarParentIndent = null

  for (const [index, raw] of source.replaceAll('\r\n', '\n').split('\n').entries()) {
    const indent = /^ */.exec(raw)[0].length
    const trimmed = raw.trim()
    let structural = true

    if (blockScalarParentIndent !== null) {
      if (trimmed === '' || indent > blockScalarParentIndent) {
        structural = false
      } else {
        blockScalarParentIndent = null
      }
    }

    const record = { raw, indent, lineNumber: index + 1, structural }
    records.push(record)

    if (
      structural
      && trimmed !== ''
      && !trimmed.startsWith('#')
      && /:\s*[>|][+-]?\s*$/.test(stripYamlComment(raw))
    ) {
      blockScalarParentIndent = indent
    }
  }

  return records
}

function cleanScalar(value) {
  let result = value.trim().replace(/\s+#.*$/, '').trim()
  if (
    result.length >= 2
    && ((result.startsWith('"') && result.endsWith('"'))
      || (result.startsWith("'") && result.endsWith("'")))
  ) {
    result = result.slice(1, -1)
  }
  return result
}

function usesEntries(source, workflowPath, violations) {
  const lines = annotateYamlLines(source)
  const entries = []

  for (const [index, line] of lines.entries()) {
    if (!line.structural || line.raw.trim() === '' || line.raw.trim().startsWith('#')) continue

    const uncommented = stripYamlComment(line.raw)
    const trimmed = uncommented.trim()
    if (trimmed === '') continue
    const mappingKeyDelimiter = trimmed.indexOf(':')
    const mappingKeyPrefix = mappingKeyDelimiter === -1
      ? trimmed
      : trimmed.slice(0, mappingKeyDelimiter)
    if (/(?:^|\s)&[^\s[\]{},]+(?:\s|$)/.test(mappingKeyPrefix)) {
      violations.add(
        `${workflowPath}:${line.lineNumber}: YAML anchors in mapping keys are unsupported by the release cache scanner`,
      )
      continue
    }
    if (/(?:^(?:-\s+)?|[{,]\s*)\?\s+/.test(trimmed)) {
      violations.add(
        `${workflowPath}:${line.lineNumber}: explicit YAML mapping keys are unsupported by the release cache scanner`,
      )
      continue
    }
    if (/(?:^(?:-\s+)?|[{,]\s*)"(?:[^"\\]|\\.)*\\(?:[^"\\]|\\.)*"\s*:/.test(trimmed)) {
      violations.add(
        `${workflowPath}:${line.lineNumber}: escaped YAML mapping keys are unsupported by the release cache scanner`,
      )
      continue
    }

    const match = /^( *)(-\s+)?(?:uses|"uses"|'uses')\s*:\s*(.*?)\s*$/.exec(uncommented)
    if (!match) {
      if (/(?:^|[{,]\s*)(?:uses|"uses"|'uses')\s*:/.test(trimmed)) {
        violations.add(`${workflowPath}:${line.lineNumber}: uses must use a static block-mapping scalar`)
      }
      continue
    }

    const propertyIndent = match[1].length + (match[2]?.length ?? 0)
    if (!match[2] && hasPrecedingSiblingWith(lines, index, propertyIndent)) {
      violations.add(`${workflowPath}:${line.lineNumber}: with must follow uses in an action step`)
    }

    entries.push({
      action: cleanScalar(match[3]),
      index,
      inputMap: readSiblingWithInputs(
        lines,
        index,
        propertyIndent,
        `${workflowPath}:${line.lineNumber}`,
        violations,
      ),
      lineNumber: line.lineNumber,
    })
  }

  return entries
}

function hasPrecedingSiblingWith(lines, usesIndex, propertyIndent) {
  for (let index = usesIndex - 1; index >= 0; index -= 1) {
    const line = lines[index]
    if (!line.structural || line.raw.trim() === '' || line.raw.trim().startsWith('#')) continue

    if (line.indent === propertyIndent && /^\s*(?:with|"with"|'with')\s*:/.test(line.raw)) {
      return true
    }

    const itemStart = /^( *)(-\s+)(?:([A-Za-z0-9_-]+)|"([A-Za-z0-9_-]+)"|'([A-Za-z0-9_-]+)')\s*:/.exec(line.raw)
    if (itemStart && itemStart[1].length + itemStart[2].length === propertyIndent) {
      return (itemStart[3] ?? itemStart[4] ?? itemStart[5]).toLowerCase() === 'with'
    }

    if (line.indent < propertyIndent) return false
  }

  return false
}

function readSiblingWithInputs(lines, usesIndex, propertyIndent, location, violations) {
  let withIndex = -1

  for (let index = usesIndex + 1; index < lines.length; index += 1) {
    const line = lines[index]
    if (!line.structural || line.raw.trim() === '' || line.raw.trim().startsWith('#')) continue
    if (line.indent < propertyIndent) break
    if (line.indent === propertyIndent) {
      const withMatch = /^\s*(?:with|"with"|'with')\s*:\s*(.*?)\s*$/.exec(line.raw)
      if (withMatch) {
        if (cleanScalar(withMatch[1]) !== '') {
          violations.add(`${location}: with must use a block mapping, not an inline scalar or flow mapping`)
          return new Map()
        }
        withIndex = index
        break
      }
    }
  }

  if (withIndex === -1) return new Map()

  const inputs = new Map()
  const withIndent = lines[withIndex].indent
  let directInputIndent = null

  for (let index = withIndex + 1; index < lines.length; index += 1) {
    const line = lines[index]
    if (line.structural && line.raw.trim() !== '' && line.indent <= withIndent) break
    if (!line.structural || line.raw.trim() === '' || line.raw.trim().startsWith('#')) continue

    if (directInputIndent === null) directInputIndent = line.indent
    if (line.indent !== directInputIndent) continue

    const match = /^\s*(?:([A-Za-z0-9_-]+)|"([A-Za-z0-9_-]+)"|'([A-Za-z0-9_-]+)')\s*:\s*(.*?)\s*$/.exec(line.raw)
    if (!match) {
      violations.add(`${location}: with inputs must use simple scalar keys in a block mapping`)
      continue
    }

    const key = (match[1] ?? match[2] ?? match[3]).toLowerCase()
    let value = cleanScalar(match[4])

    if (/^[>|][+-]?$/.test(value)) {
      const blockValues = []
      for (let blockIndex = index + 1; blockIndex < lines.length; blockIndex += 1) {
        const blockLine = lines[blockIndex]
        if (blockLine.raw.trim() !== '' && blockLine.indent <= line.indent) break
        if (blockLine.raw.trim() !== '' && !blockLine.raw.trim().startsWith('#')) {
          blockValues.push(blockLine.raw.trim())
        }
      }
      value = blockValues.join('\n')
    }

    if (inputs.has(key)) violations.add(`${location}: duplicate with input ${key} is ambiguous`)
    inputs.set(key, value)
  }

  return inputs
}

function discoverReleaseRoots(sources, violations) {
  const workflowNames = [...sources.keys()]
    .filter((path) => path.startsWith(workflowPrefix) && !path.slice(workflowPrefix.length).includes('/'))
    .map((path) => path.slice(workflowPrefix.length))
    .sort()

  if (!workflowNames.includes('ci-release.yml')) {
    violations.add(`${workflowPrefix}ci-release.yml: required release root is missing`)
  }

  const releaseRoots = workflowNames.filter((name) => /^release-.*\.yml$/.test(name))
  if (releaseRoots.length === 0) {
    violations.add(`${workflowPrefix}release-*.yml: no release workflow roots were discovered`)
  }

  return ['ci-release.yml', ...releaseRoots]
    .filter((name, index, names) => names.indexOf(name) === index)
    .map((name) => `${workflowPrefix}${name}`)
}

function localReusableCallees(source, workflowPath, violations) {
  const callees = []

  for (const entry of usesEntries(source, workflowPath, violations)) {
    if (!entry.action.startsWith('./.github/workflows/')) continue

    if (!/^\.\/\.github\/workflows\/[^/]+\.yml$/.test(entry.action)) {
      violations.add(
        `${workflowPath}:${entry.lineNumber}: local reusable workflow must be a static ./.github/workflows/*.yml path`,
      )
      continue
    }

    callees.push(entry.action.slice(2))
  }

  return callees
}

function inspectAction(entry, workflowPath, violations) {
  const location = `${workflowPath}:${entry.lineNumber}`
  if (entry.action.startsWith('./.github/workflows/')) return
  if (entry.action.startsWith('./')) {
    violations.add(`${location}: local actions are outside the recursively verified release-workflow closure`)
    return
  }

  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*@[^\s@]+$/.test(entry.action)) {
    violations.add(`${location}: uses must name one static owner/repository action reference with an @ref`)
    return
  }

  const actionId = entry.action.split('@', 1)[0].toLowerCase()
  const inputs = entry.inputMap

  if (['actions/cache', 'actions/cache/restore', 'actions/cache/save'].includes(actionId)) {
    violations.add(`${location}: ${actionId} may not restore or publish caches in a release workflow`)
  }

  if (actionId === 'docker/build-push-action') {
    for (const forbiddenInput of ['cache-from', 'cache-to']) {
      if (inputs.has(forbiddenInput)) {
        violations.add(`${location}: docker/build-push-action may not declare ${forbiddenInput} in a release workflow`)
      }
    }
    if (cleanScalar(inputs.get('no-cache') ?? '').toLowerCase() !== 'true') {
      violations.add(`${location}: docker/build-push-action must declare no-cache: true in a release workflow`)
    }
  }

  const cacheInputs = [...inputs.keys()].filter((key) => key === 'cache' || key.startsWith('cache-'))
  const isDependencySetupAction = ['actions/setup-node', 'actions/setup-dotnet'].includes(actionId)
  if (isDependencySetupAction && inputs.has('cache')) {
    const cacheSetting = cleanScalar(inputs.get('cache')).toLowerCase()
    const cacheEnabled = !['', 'false', 'none', 'off'].includes(cacheSetting)
    if (cacheEnabled && cleanScalar(inputs.get('cache-dependency-path') ?? '') === '') {
      violations.add(`${location}: ${actionId} cache must declare a non-empty cache-dependency-path`)
    }
  } else if (!isDependencySetupAction && actionId !== 'docker/build-push-action' && cacheInputs.length > 0) {
    violations.add(
      `${location}: cache inputs (${cacheInputs.join(', ')}) are allowed only for lockfile-bound setup-node/setup-dotnet dependency caches`,
    )
  }

  if (actionId === 'actions/download-artifact') {
    for (const crossRunInput of ['github-token', 'repository', 'run-id']) {
      if (inputs.has(crossRunInput)) {
        violations.add(`${location}: actions/download-artifact may not declare cross-run input ${crossRunInput}`)
      }
    }
  } else if (/download.*artifact|artifact.*download/.test(actionId)) {
    violations.add(`${location}: third-party artifact download action ${actionId} is forbidden in a release workflow`)
  }
}

function analyzeReleaseCacheContract(sources) {
  const violations = new Set()
  const roots = discoverReleaseRoots(sources, violations)
  const visited = new Set()
  const visiting = []

  function visit(workflowPath) {
    const cycleAt = visiting.indexOf(workflowPath)
    if (cycleAt !== -1) {
      violations.add(`release workflow cycle: ${[...visiting.slice(cycleAt), workflowPath].join(' -> ')}`)
      return
    }
    if (visited.has(workflowPath)) return

    const source = sources.get(workflowPath)
    if (source === undefined) {
      violations.add(`${workflowPath}: referenced release workflow is missing`)
      return
    }

    visiting.push(workflowPath)
    for (const callee of localReusableCallees(source, workflowPath, violations)) visit(callee)
    visiting.pop()
    visited.add(workflowPath)
  }

  for (const root of roots) visit(root)

  for (const workflowPath of [...visited].sort()) {
    for (const entry of usesEntries(sources.get(workflowPath), workflowPath, violations)) {
      inspectAction(entry, workflowPath, violations)
    }
  }

  return {
    closure: [...visited].sort(),
    roots: roots.filter((root) => sources.has(root)).sort(),
    violations: [...violations].sort(),
  }
}

function enforceReleaseCacheContract(sources) {
  const result = analyzeReleaseCacheContract(sources)
  if (result.violations.length > 0) {
    throw new Error(`Release cache contract violations:\n- ${result.violations.join('\n- ')}`)
  }
  return result
}

function validSyntheticClosure() {
  return new Map([
    [`${workflowPrefix}ci-release.yml`, `
name: CI Release
on: workflow_dispatch
jobs:
  verify:
    uses: ./.github/workflows/reusable-release-build.yml
`],
    [`${workflowPrefix}release-container.yml`, `
name: Release Container
on: workflow_dispatch
jobs:
  image:
    runs-on: ubuntu-latest
    steps:
      - uses: docker/build-push-action@v7
        with:
          context: .
          no-cache: true
`],
    [`${workflowPrefix}reusable-release-build.yml`, `
name: Reusable release build
on: workflow_call
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-node@v7
        with:
          cache: npm
          cache-dependency-path: package-lock.json
      - uses: actions/download-artifact@v8
        with:
          name: release-input
`],
  ])
}

test('the repository release closure restores no untrusted build output', () => {
  const result = enforceReleaseCacheContract(workflowSourcesFromDisk())

  assert.ok(result.roots.includes(`${workflowPrefix}ci-release.yml`))
  assert.ok(result.roots.includes(`${workflowPrefix}release-container.yml`))
  assert.ok(result.closure.includes(`${workflowPrefix}reusable-container-images.yml`))
  assert.ok(result.closure.includes(`${workflowPrefix}reusable-sbom-provenance.yml`))
})

test('forbidden cache mutations fail closed', () => {
  const mutations = [
    {
      label: 'the cache action',
      expected: /actions\/cache may not restore or publish caches/,
      mutate(sources) {
        sources.set(`${workflowPrefix}release-security.yml`, `
name: Release Security
on: workflow_dispatch
jobs:
  unsafe:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/cache@v5
`)
      },
    },
    {
      label: 'a BuildKit cache input',
      expected: /may not declare cache-from/,
      mutate(sources) {
        sources.set(
          `${workflowPrefix}release-container.yml`,
          sources.get(`${workflowPrefix}release-container.yml`).replace('no-cache: true', 'no-cache: true\n          cache-from: type=gha'),
        )
      },
    },
    {
      label: 'a release build without an explicit clean build',
      expected: /must declare no-cache: true/,
      mutate(sources) {
        sources.set(
          `${workflowPrefix}release-container.yml`,
          sources.get(`${workflowPrefix}release-container.yml`).replace('          no-cache: true\n', ''),
        )
      },
    },
    {
      label: 'a dependency cache without a lockfile path',
      expected: /cache must declare a non-empty cache-dependency-path/,
      mutate(sources) {
        sources.set(
          `${workflowPrefix}reusable-release-build.yml`,
          sources.get(`${workflowPrefix}reusable-release-build.yml`).replace('          cache-dependency-path: package-lock.json\n', ''),
        )
      },
    },
  ]

  for (const mutation of mutations) {
    const sources = validSyntheticClosure()
    mutation.mutate(sources)
    assert.throws(() => enforceReleaseCacheContract(sources), mutation.expected, mutation.label)
  }
})

test('unsafe artifact promotion mutations fail closed', () => {
  for (const [unsafeInput, renderedInput] of [
    ['github-token', 'github-token'],
    ['repository', 'repository'],
    ['run-id', '"run-id"'],
  ]) {
    const sources = validSyntheticClosure()
    sources.set(
      `${workflowPrefix}reusable-release-build.yml`,
      sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
        '          name: release-input',
        `          name: release-input\n          ${renderedInput}: attacker-controlled`,
      ),
    )
    assert.throws(
      () => enforceReleaseCacheContract(sources),
      new RegExp(`may not declare cross-run input ${unsafeInput}`),
    )
  }

  const thirdPartySources = validSyntheticClosure()
  thirdPartySources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    thirdPartySources.get(`${workflowPrefix}reusable-release-build.yml`)
      .replace('actions/download-artifact@v8', 'dawidd6/action-download-artifact@v11'),
  )
  assert.throws(
    () => enforceReleaseCacheContract(thirdPartySources),
    /third-party artifact download action dawidd6\/action-download-artifact is forbidden/,
  )
})

test('with-before-uses cannot hide cross-run artifact inputs', () => {
  const sources = validSyntheticClosure()
  sources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      `      - uses: actions/download-artifact@v8
        with:
          name: release-input`,
      `      - with:
          name: release-input
          github-token: attacker-controlled
          repository: attacker/repository
          run-id: 123
        uses: actions/download-artifact@v8`,
    ),
  )

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /with must follow uses in an action step/,
  )
})

test('irregular valid sequence whitespace cannot hide cross-run artifact inputs', () => {
  const sources = validSyntheticClosure()
  sources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      `      - uses: actions/download-artifact@v8
        with:
          name: release-input`,
      `      -  uses: actions/download-artifact@v8
         with:
           name: release-input
           run-id: 123`,
    ),
  )

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /actions\/download-artifact may not declare cross-run input run-id/,
  )
})

test('anchored action mappings cannot hide cross-run artifact inputs', () => {
  const sources = validSyntheticClosure()
  sources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      `      - uses: actions/download-artifact@v8
        with:
          name: release-input`,
      `      - &unsafe uses: actions/download-artifact@v8
        with:
          name: release-input
          run-id: 123`,
    ),
  )

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /YAML anchors in mapping keys are unsupported by the release cache scanner/,
  )
})

test('encoded and explicit action keys cannot hide cross-run artifact inputs', () => {
  const cases = [
    {
      label: 'escaped double-quoted key',
      usesLine: '      - "\\u0075ses": actions/download-artifact@v8',
      expected: /escaped YAML mapping keys are unsupported by the release cache scanner/,
    },
    {
      label: 'anchored escaped double-quoted key',
      usesLine: '      - &unsafe "\\u0075ses": actions/download-artifact@v8',
      expected: /YAML anchors in mapping keys are unsupported by the release cache scanner/,
    },
    {
      label: 'explicit mapping key',
      usesLine: `      - ? uses
        : actions/download-artifact@v8`,
      expected: /explicit YAML mapping keys are unsupported by the release cache scanner/,
    },
  ]

  for (const testCase of cases) {
    const sources = validSyntheticClosure()
    sources.set(
      `${workflowPrefix}reusable-release-build.yml`,
      sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
        '      - uses: actions/download-artifact@v8',
        testCase.usesLine,
      ).replace(
        '          name: release-input',
        `          name: release-input
          run-id: 123`,
      ),
    )

    assert.throws(
      () => enforceReleaseCacheContract(sources),
      testCase.expected,
      testCase.label,
    )
  }
})

test('escaped explicit keys in flow mappings cannot hide cross-run artifact inputs', () => {
  const sources = validSyntheticClosure()
  sources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      `      - uses: actions/download-artifact@v8
        with:
          name: release-input`,
      '      - { ? "\\u0075ses" : actions/download-artifact@v8, with: {name: release-input, run-id: 123}}',
    ),
  )

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /explicit YAML mapping keys are unsupported by the release cache scanner/,
  )

  const commentSources = validSyntheticClosure()
  commentSources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    commentSources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      '          name: release-input',
      '          name: release-input # { ? "\\u0075ses" : ignored }',
    ),
  )
  assert.doesNotThrow(() => enforceReleaseCacheContract(commentSources))
})

test('plain-scalar comments cannot disguise action lines as block-scalar content', () => {
  const sources = validSyntheticClosure()
  sources.set(
    `${workflowPrefix}reusable-release-build.yml`,
    sources.get(`${workflowPrefix}reusable-release-build.yml`).replace(
      `      - uses: actions/setup-node@v7
        with:
          cache: npm
          cache-dependency-path: package-lock.json`,
      `      - name: disguise # : |
        uses: actions/cache@v5`,
    ),
  )

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /actions\/cache may not restore or publish caches/,
  )
})

test('a violation hidden in a transitive reusable workflow fails closed', () => {
  const sources = validSyntheticClosure()
  sources.set(`${workflowPrefix}reusable-release-build.yml`, `
name: Reusable release build
on: workflow_call
jobs:
  deeper:
    uses: ./.github/workflows/reusable-release-deep.yml
`)
  sources.set(`${workflowPrefix}reusable-release-deep.yml`, `
name: Deep reusable release build
on: workflow_call
jobs:
  unsafe:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/cache/save@v5
`)

  assert.throws(
    () => enforceReleaseCacheContract(sources),
    /reusable-release-deep\.yml:.*actions\/cache\/save may not restore or publish caches/,
  )
})

test('missing callees and reusable-workflow cycles fail closed', () => {
  const missingSources = validSyntheticClosure()
  missingSources.set(`${workflowPrefix}reusable-release-build.yml`, `
name: Missing release call
on: workflow_call
jobs:
  missing:
    uses: ./.github/workflows/reusable-release-missing.yml
`)
  assert.throws(
    () => enforceReleaseCacheContract(missingSources),
    /reusable-release-missing\.yml: referenced release workflow is missing/,
  )

  const cyclicSources = validSyntheticClosure()
  cyclicSources.set(`${workflowPrefix}reusable-release-build.yml`, `
name: Cyclic release call
on: workflow_call
jobs:
  cycle:
    uses: ./.github/workflows/ci-release.yml
`)
  assert.throws(
    () => enforceReleaseCacheContract(cyclicSources),
    /release workflow cycle: .*ci-release\.yml.*reusable-release-build\.yml.*ci-release\.yml/,
  )
})
