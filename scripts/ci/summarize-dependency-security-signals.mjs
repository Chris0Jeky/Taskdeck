#!/usr/bin/env node

import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, relative, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

const SUPPORTED_ADVISORY_ID_PATTERN =
  /^(?:GHSA-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}|CVE-\d{4}-\d{4,}|NPM-\d+|https:\/\/\S+)$/i

function parseArgs(argv) {
  const args = new Map()
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index]
    if (!token.startsWith('--')) {
      throw new Error(`Unexpected argument: ${token}`)
    }

    const next = argv[index + 1]
    if (next === undefined || next.startsWith('--')) {
      throw new Error(`Missing value for ${token}`)
    }

    args.set(token.slice(2), next)
    index += 1
  }

  return args
}

async function readTextAuto(path) {
  const buffer = await readFile(path)
  if (buffer.length >= 2 && buffer[0] === 0xff && buffer[1] === 0xfe) {
    return buffer.subarray(2).toString('utf16le')
  }

  if (buffer.length >= 2 && buffer[0] === 0xfe && buffer[1] === 0xff) {
    const swapped = Buffer.allocUnsafe(buffer.length - 2)
    for (let index = 2; index < buffer.length; index += 2) {
      swapped[index - 2] = buffer[index + 1]
      swapped[index - 1] = buffer[index]
    }
    return swapped.toString('utf16le')
  }

  return buffer.toString('utf8').replace(/^\uFEFF/, '')
}

async function readJsonIfPresent(path) {
  if (!path) {
    return null
  }

  const text = await readTextAuto(path)
  return JSON.parse(text)
}

async function readExitCode(path) {
  if (!path) {
    return 0
  }

  const text = await readTextAuto(path)
  return Number.parseInt(text.trim(), 10)
}

function normalizeSeverity(value) {
  const normalized = String(value ?? '').trim().toLowerCase()
  if (normalized === 'critical') {
    return 'critical'
  }
  if (normalized === 'high') {
    return 'high'
  }
  if (normalized === 'moderate') {
    return 'moderate'
  }
  if (normalized === 'low') {
    return 'low'
  }
  return 'unknown'
}

function isHighOrCritical(severity) {
  return severity === 'high' || severity === 'critical'
}

function createSeverityCounts() {
  return {
    critical: 0,
    high: 0,
    moderate: 0,
    low: 0,
    unknown: 0,
  }
}

function readNonNegativeInteger(value, fieldName) {
  if (value === undefined || value === null) {
    return 0
  }
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error(`frontend audit report has an invalid ${fieldName} vulnerability count`)
  }
  return value
}

function isSupportedAdvisoryId(value) {
  return typeof value === 'string' && SUPPORTED_ADVISORY_ID_PATTERN.test(value.trim())
}

function normalizeAdvisoryId(value) {
  if (typeof value !== 'string') {
    return null
  }

  const text = value.trim()
  if (!text) {
    return null
  }

  const ghsa = text.match(/GHSA-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}/i)
  if (ghsa) {
    return ghsa[0].toUpperCase()
  }

  const cve = text.match(/CVE-\d{4}-\d{4,}/i)
  if (cve) {
    return cve[0].toUpperCase()
  }

  if (/^NPM-\d+$/i.test(text)) {
    return text.toUpperCase()
  }

  if (/^https:\/\//i.test(text)) {
    return text
  }

  return null
}

function advisoryIdFromNpmVia(via) {
  if (!via || typeof via !== 'object' || Array.isArray(via)) {
    return null
  }

  const fromUrl = normalizeAdvisoryId(via.url)
  if (fromUrl) {
    return fromUrl
  }

  if (Number.isInteger(via.source)) {
    return `NPM-${via.source}`
  }

  return normalizeAdvisoryId(via.advisoryId)
}

function isValidIsoDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(value ?? ''))) {
    return false
  }

  const parsed = new Date(`${value}T00:00:00.000Z`)
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value
}

function validateAllowlistDocument(document, path, today) {
  const errors = []
  const entries = []
  const seenIds = new Set()

  if (!document || typeof document !== 'object' || Array.isArray(document)) {
    errors.push('allowlist must be a JSON object')
  } else {
    const topLevelKeys = new Set(['$schema', 'schemaVersion', 'entries'])
    for (const key of Object.keys(document)) {
      if (!topLevelKeys.has(key)) {
        errors.push(`unexpected top-level property: ${key}`)
      }
    }

    if (document.schemaVersion !== 1) {
      errors.push('schemaVersion must be 1')
    }
    if (!Array.isArray(document.entries)) {
      errors.push('entries must be an array')
    } else {
      for (const [index, rawEntry] of document.entries.entries()) {
        const prefix = `entries[${index}]`
        if (!rawEntry || typeof rawEntry !== 'object' || Array.isArray(rawEntry)) {
          errors.push(`${prefix} must be an object`)
          continue
        }

        const entryKeys = new Set(['advisoryId', 'reason', 'owner', 'expiresOn'])
        for (const key of Object.keys(rawEntry)) {
          if (!entryKeys.has(key)) {
            errors.push(`${prefix} has unexpected property: ${key}`)
          }
        }

        const rawAdvisoryId = rawEntry.advisoryId
        const advisoryIdText = typeof rawAdvisoryId === 'string' ? rawAdvisoryId.trim() : ''
        const advisoryId = normalizeAdvisoryId(rawAdvisoryId)
        const supportedAdvisoryId = isSupportedAdvisoryId(rawAdvisoryId)
        const reason = String(rawEntry.reason ?? '').trim()
        const owner = String(rawEntry.owner ?? '').trim()
        const expiresOn = String(rawEntry.expiresOn ?? '').trim()
        let entryValid = true

        if (typeof rawAdvisoryId !== 'string') {
          errors.push(`${prefix}.advisoryId must be a string`)
          entryValid = false
        } else if (!advisoryIdText) {
          errors.push(`${prefix}.advisoryId must be non-empty`)
          entryValid = false
        } else if (!supportedAdvisoryId || !advisoryId) {
          errors.push(`${prefix}.advisoryId must be a supported advisory identifier`)
          entryValid = false
        } else if (seenIds.has(advisoryId)) {
          errors.push(`${prefix}.advisoryId duplicates ${advisoryId}`)
          entryValid = false
        }
        if (!reason) {
          errors.push(`${prefix}.reason must be non-empty`)
          entryValid = false
        }
        if (!owner) {
          errors.push(`${prefix}.owner must be non-empty`)
          entryValid = false
        }
        if (!isValidIsoDate(expiresOn)) {
          errors.push(`${prefix}.expiresOn must be a real YYYY-MM-DD date`)
          entryValid = false
        }

        if (supportedAdvisoryId && advisoryId) {
          seenIds.add(advisoryId)
        }
        if (entryValid) {
          entries.push({ advisoryId, reason, owner, expiresOn })
        }
      }
    }
  }

  const activeEntries = entries.filter((entry) => entry.expiresOn >= today)
  const expiredEntries = entries.filter((entry) => entry.expiresOn < today)
  const activeById = new Map(activeEntries.map((entry) => [entry.advisoryId, entry]))

  return {
    summary: {
      configured: true,
      path: toRepoRelative(path),
      valid: errors.length === 0,
      parseFailed: false,
      errorCount: errors.length,
      errors,
      activeEntryCount: activeEntries.length,
      expiredEntryCount: expiredEntries.length,
      activeEntries,
      expiredEntries,
    },
    activeById: errors.length === 0 ? activeById : new Map(),
  }
}

async function loadAllowlist(path, today) {
  if (!path) {
    return {
      summary: {
        configured: false,
        path: null,
        valid: true,
        parseFailed: false,
        errorCount: 0,
        errors: [],
        activeEntryCount: 0,
        expiredEntryCount: 0,
        activeEntries: [],
        expiredEntries: [],
      },
      activeById: new Map(),
    }
  }

  try {
    const document = await readJsonIfPresent(path)
    return validateAllowlistDocument(document, path, today)
  } catch (error) {
    return {
      summary: {
        configured: true,
        path: toRepoRelative(path),
        valid: false,
        parseFailed: true,
        errorCount: 1,
        errors: [error instanceof Error ? error.message : String(error)],
        activeEntryCount: 0,
        expiredEntryCount: 0,
        activeEntries: [],
        expiredEntries: [],
      },
      activeById: new Map(),
    }
  }
}

function summarizeBackendReport(report, exitCode, activeAllowlist, matchedAdvisories) {
  if (
    !report ||
    typeof report !== 'object' ||
    Array.isArray(report) ||
    !Array.isArray(report.projects)
  ) {
    throw new Error('backend vulnerability report is missing a projects array')
  }

  const severityCounts = createSeverityCounts()
  const packages = new Map()
  let acceptedHighOrCriticalCount = 0
  let unresolvedHighOrCriticalCount = 0

  for (const project of report.projects) {
    for (const framework of project.frameworks ?? []) {
      for (const packageSetName of ['topLevelPackages', 'transitivePackages']) {
        for (const pkg of framework[packageSetName] ?? []) {
          if (!Array.isArray(pkg.vulnerabilities) || pkg.vulnerabilities.length === 0) {
            continue
          }

          const key = `${pkg.id}@${pkg.resolvedVersion ?? 'unknown'}`
          const packageSummary = packages.get(key) ?? {
            id: pkg.id,
            version: pkg.resolvedVersion ?? 'unknown',
            projects: new Set(),
            severities: new Set(),
            advisoryIds: new Set(),
            acceptedAdvisoryIds: new Set(),
          }

          packageSummary.projects.add(project.path)

          for (const vulnerability of pkg.vulnerabilities) {
            const severity = normalizeSeverity(vulnerability.severity)
            const advisoryId = normalizeAdvisoryId(vulnerability.advisoryurl)
            severityCounts[severity] += 1
            packageSummary.severities.add(severity)
            if (advisoryId) {
              packageSummary.advisoryIds.add(advisoryId)
            }

            if (isHighOrCritical(severity)) {
              if (advisoryId && activeAllowlist.has(advisoryId)) {
                acceptedHighOrCriticalCount += 1
                packageSummary.acceptedAdvisoryIds.add(advisoryId)
                matchedAdvisories.add(advisoryId)
              } else {
                unresolvedHighOrCriticalCount += 1
              }
            }
          }

          packages.set(key, packageSummary)
        }
      }
    }
  }

  const packageList = [...packages.values()]
    .map((pkg) => ({
      id: pkg.id,
      version: pkg.version,
      severities: [...pkg.severities].sort(),
      advisoryIds: [...pkg.advisoryIds].sort(),
      acceptedAdvisoryIds: [...pkg.acceptedAdvisoryIds].sort(),
      projectCount: pkg.projects.size,
    }))
    .sort((left, right) => left.id.localeCompare(right.id))

  return {
    exitCode,
    scanFailed: exitCode !== 0,
    packageCount: packageList.length,
    severityCounts,
    packages: packageList,
    highOrCriticalCount: severityCounts.high + severityCounts.critical,
    acceptedHighOrCriticalCount,
    unresolvedHighOrCriticalCount,
    hasFindings: packageList.length > 0,
    parseFailed: false,
  }
}

const SEVERITY_RANK = {
  unknown: 0,
  low: 1,
  moderate: 2,
  high: 3,
  critical: 4,
}

function mergeAdvisory(advisories, advisoryId, severity) {
  if (!advisoryId) {
    return
  }

  const normalizedSeverity = normalizeSeverity(severity)
  const existing = advisories.get(advisoryId)
  if (!existing || SEVERITY_RANK[normalizedSeverity] > SEVERITY_RANK[existing]) {
    advisories.set(advisoryId, normalizedSeverity)
  }
}

function collectFrontendAdvisories(packageName, vulnerabilities, cache, visiting = new Set()) {
  if (cache.has(packageName)) {
    return new Map(cache.get(packageName))
  }
  if (visiting.has(packageName)) {
    return new Map()
  }

  const entry = vulnerabilities[packageName]
  if (!entry || typeof entry !== 'object') {
    return new Map()
  }

  const nextVisiting = new Set(visiting)
  nextVisiting.add(packageName)
  const advisories = new Map()

  for (const via of Array.isArray(entry.via) ? entry.via : []) {
    if (typeof via === 'string') {
      const nested = collectFrontendAdvisories(via, vulnerabilities, cache, nextVisiting)
      for (const [advisoryId, severity] of nested) {
        mergeAdvisory(advisories, advisoryId, severity)
      }
      continue
    }

    const advisoryId = advisoryIdFromNpmVia(via)
    mergeAdvisory(advisories, advisoryId, via?.severity ?? entry.severity)
  }

  cache.set(packageName, new Map(advisories))
  return advisories
}

function summarizeFrontendReport(report, exitCode, activeAllowlist, matchedAdvisories) {
  const hasAuditShape =
    report &&
    typeof report === 'object' &&
    !Array.isArray(report) &&
    report.vulnerabilities &&
    typeof report.vulnerabilities === 'object' &&
    !Array.isArray(report.vulnerabilities) &&
    report.metadata?.vulnerabilities &&
    typeof report.metadata.vulnerabilities === 'object' &&
    !Array.isArray(report.metadata.vulnerabilities)
  if (!hasAuditShape) {
    throw new Error('frontend audit report is missing npm audit vulnerability metadata')
  }

  const metadataCounts = report.metadata.vulnerabilities
  readNonNegativeInteger(metadataCounts.info, 'info')
  const severityCounts = {
    critical: readNonNegativeInteger(metadataCounts.critical, 'critical'),
    high: readNonNegativeInteger(metadataCounts.high, 'high'),
    moderate: readNonNegativeInteger(metadataCounts.moderate, 'moderate'),
    low: readNonNegativeInteger(metadataCounts.low, 'low'),
    unknown: 0,
  }
  const totalCount = readNonNegativeInteger(metadataCounts.total, 'total')
  const vulnerabilities = report.vulnerabilities
  const advisoryCache = new Map()
  let acceptedHighOrCriticalCount = 0
  let packageLevelUnresolvedCount = 0

  const packages = Object.values(vulnerabilities)
    .map((entry) => {
      const severity = normalizeSeverity(entry.severity)
      const advisories = collectFrontendAdvisories(entry.name, vulnerabilities, advisoryCache)
      const advisoryIds = [...advisories.keys()].sort()
      const enforcedAdvisoryIds = [...advisories.entries()]
        .filter(([, advisorySeverity]) => isHighOrCritical(advisorySeverity))
        .map(([advisoryId]) => advisoryId)
        .sort()
      const isEnforcedFinding = isHighOrCritical(severity)
      const accepted =
        isEnforcedFinding &&
        enforcedAdvisoryIds.length > 0 &&
        enforcedAdvisoryIds.every((advisoryId) => activeAllowlist.has(advisoryId))

      if (accepted) {
        acceptedHighOrCriticalCount += 1
        for (const advisoryId of enforcedAdvisoryIds) {
          matchedAdvisories.add(advisoryId)
        }
      } else if (isEnforcedFinding) {
        packageLevelUnresolvedCount += 1
      }

      return {
        id: entry.name,
        severity,
        isDirect: Boolean(entry.isDirect),
        fixAvailable: entry.fixAvailable === true || typeof entry.fixAvailable === 'object',
        advisoryIds,
        acceptedAdvisoryIds: accepted ? enforcedAdvisoryIds : [],
      }
    })
    .sort((left, right) => left.id.localeCompare(right.id))

  const highOrCriticalCount = severityCounts.high + severityCounts.critical
  const unresolvedHighOrCriticalCount = Math.max(
    highOrCriticalCount - acceptedHighOrCriticalCount,
    packageLevelUnresolvedCount,
  )
  const hasFindings = totalCount > 0
  const structuredError = Boolean(report.error)
  const findingExit = exitCode === 1 && hasFindings && !structuredError

  return {
    exitCode,
    scanFailed: structuredError || (exitCode !== 0 && !findingExit),
    packageCount: packages.length,
    severityCounts,
    packages,
    highOrCriticalCount,
    acceptedHighOrCriticalCount,
    unresolvedHighOrCriticalCount,
    hasFindings,
    parseFailed: false,
  }
}

function summarizeScanFailure(exitCode) {
  return {
    exitCode,
    scanFailed: true,
    packageCount: 0,
    severityCounts: createSeverityCounts(),
    packages: [],
    highOrCriticalCount: 0,
    acceptedHighOrCriticalCount: 0,
    unresolvedHighOrCriticalCount: 0,
    hasFindings: false,
    parseFailed: true,
  }
}

function formatSeverityCounts(counts) {
  return `critical ${counts.critical}, high ${counts.high}, moderate ${counts.moderate}, low ${counts.low}, unknown ${counts.unknown}`
}

function formatBackendPackage(pkg) {
  const advisories = pkg.advisoryIds.length > 0 ? `, advisories=${pkg.advisoryIds.join(',')}` : ''
  return `${pkg.id}@${pkg.version} (${pkg.severities.join('/')}, projects=${pkg.projectCount}${advisories})`
}

function formatFrontendPackage(pkg) {
  const fixStatus = pkg.fixAvailable ? 'fix available' : 'no fix'
  const directness = pkg.isDirect ? 'direct' : 'transitive'
  const advisories = pkg.advisoryIds.length > 0 ? `, advisories=${pkg.advisoryIds.join(',')}` : ''
  return `${pkg.id} (${pkg.severity}, ${directness}, ${fixStatus}${advisories})`
}

function toRepoRelative(path) {
  return relative(process.cwd(), resolve(path)).replaceAll('\\', '/')
}

function hasScanFailure(scan) {
  return scan.scanFailed || scan.parseFailed
}

function formatAdvisoryEntries(entries) {
  if (entries.length === 0) {
    return 'none'
  }
  return entries.map((entry) => `${entry.advisoryId} (owner=${entry.owner}, expires=${entry.expiresOn})`).join('; ')
}

export async function buildSummary(options) {
  const backendExitCode = await readExitCode(options.backendExitCodeFile)
  const frontendExitCode = await readExitCode(options.frontendExitCodeFile)
  const today = options.today ?? new Date().toISOString().slice(0, 10)
  if (!isValidIsoDate(today)) {
    throw new Error(`today must be a real YYYY-MM-DD date: ${today}`)
  }

  const { summary: allowlist, activeById } = await loadAllowlist(options.allowlist, today)
  const matchedAdvisories = new Set()

  let backend
  try {
    backend = summarizeBackendReport(
      await readJsonIfPresent(options.backendReport),
      backendExitCode,
      activeById,
      matchedAdvisories,
    )
  } catch {
    backend = summarizeScanFailure(backendExitCode)
  }

  let frontend
  try {
    frontend = summarizeFrontendReport(
      await readJsonIfPresent(options.frontendReport),
      frontendExitCode,
      activeById,
      matchedAdvisories,
    )
  } catch {
    frontend = summarizeScanFailure(frontendExitCode)
  }

  allowlist.matchedActiveEntryCount = [...matchedAdvisories].filter((id) => activeById.has(id)).length
  allowlist.unusedActiveAdvisoryIds = [...activeById.keys()]
    .filter((id) => !matchedAdvisories.has(id))
    .sort()

  const highOrCriticalFindings = backend.highOrCriticalCount + frontend.highOrCriticalCount
  const acceptedHighOrCriticalFindings =
    backend.acceptedHighOrCriticalCount + frontend.acceptedHighOrCriticalCount
  const unresolvedHighOrCriticalFindings =
    backend.unresolvedHighOrCriticalCount + frontend.unresolvedHighOrCriticalCount
  const scanFailures = Number(hasScanFailure(backend)) + Number(hasScanFailure(frontend))
  const parseFailures = Number(backend.parseFailed) + Number(frontend.parseFailed)
  const allowlistFailures = Number(!allowlist.valid) + allowlist.expiredEntryCount

  const summary = {
    summaryTitle: options.summaryTitle,
    workflowContext: options.workflowContext,
    policyDoc: options.policyDoc ? toRepoRelative(options.policyDoc) : null,
    allowlist,
    backend,
    frontend,
    totals: {
      highOrCriticalFindings,
      acceptedHighOrCriticalFindings,
      unresolvedHighOrCriticalFindings,
      scanFailures,
      parseFailures,
      allowlistFailures,
      hasActionableFindings: unresolvedHighOrCriticalFindings > 0,
      hasEnforcementFailures:
        unresolvedHighOrCriticalFindings > 0 || scanFailures > 0 || allowlistFailures > 0,
    },
  }

  const allowlistStatus = allowlist.configured
    ? allowlist.valid
      ? 'valid'
      : 'invalid'
    : 'not configured'
  const markdownLines = [
    `## ${options.summaryTitle}`,
    '',
    `- Workflow context: ${options.workflowContext}`,
    options.policyDoc ? `- Policy: \`${toRepoRelative(options.policyDoc)}\`` : null,
    allowlist.path ? `- Advisory allowlist: \`${allowlist.path}\`` : null,
    '- Release-blocking threshold: unresolved high/critical dependency findings, operational scan failures, parse failures, invalid allowlist configuration, or expired exceptions in an enforcement run.',
    '',
    '### Advisory Exceptions',
    `- Allowlist status: ${allowlistStatus}`,
    `- Active exceptions: ${allowlist.activeEntryCount}`,
    `- Expired exceptions: ${allowlist.expiredEntryCount}`,
    `- Matched active exceptions: ${allowlist.matchedActiveEntryCount}`,
    `- Accepted high/critical findings: ${acceptedHighOrCriticalFindings}`,
    `- Unresolved high/critical findings: ${unresolvedHighOrCriticalFindings}`,
    `- Active exception records: ${formatAdvisoryEntries(allowlist.activeEntries)}`,
    `- Expired exception records: ${formatAdvisoryEntries(allowlist.expiredEntries)}`,
    allowlist.errors.length > 0 ? `- Validation errors: ${allowlist.errors.join('; ')}` : '- Validation errors: none',
    allowlist.unusedActiveAdvisoryIds.length > 0
      ? `- Unused active advisory IDs: ${allowlist.unusedActiveAdvisoryIds.join(', ')}`
      : '- Unused active advisory IDs: none',
    '',
    '### Backend',
    `- Exit code: ${backend.exitCode}`,
    `- Scan status: ${backend.scanFailed ? 'failed' : 'ok'}`,
    `- Parse status: ${backend.parseFailed ? 'failed' : 'ok'}`,
    `- Vulnerable packages: ${backend.packageCount}`,
    `- Severity counts: ${formatSeverityCounts(backend.severityCounts)}`,
    `- Accepted high/critical findings: ${backend.acceptedHighOrCriticalCount}`,
    `- Unresolved high/critical findings: ${backend.unresolvedHighOrCriticalCount}`,
    backend.packages.length > 0 ? `- Top packages: ${backend.packages.slice(0, 5).map(formatBackendPackage).join('; ')}` : '- Top packages: none',
    '',
    '### Frontend',
    `- Exit code: ${frontend.exitCode}`,
    `- Scan status: ${frontend.scanFailed ? 'failed' : 'ok'}`,
    `- Parse status: ${frontend.parseFailed ? 'failed' : 'ok'}`,
    `- Vulnerable packages: ${frontend.packageCount}`,
    `- Severity counts: ${formatSeverityCounts(frontend.severityCounts)}`,
    `- Accepted high/critical findings: ${frontend.acceptedHighOrCriticalCount}`,
    `- Unresolved high/critical findings: ${frontend.unresolvedHighOrCriticalCount}`,
    frontend.packages.length > 0 ? `- Top packages: ${frontend.packages.slice(0, 5).map(formatFrontendPackage).join('; ')}` : '- Top packages: none',
    '',
    '### Triage Guidance',
    '- Owner: maintainers of the touched dependency surface with security review by repository maintainers.',
    '- Required action: remediate, pin/upgrade safely, or add one identified advisory exception with owner, rationale, and expiry.',
    '- Unknown advisory identities cannot be excepted. Expired or malformed exception records fail closed.',
    '- SLA reference: see the policy doc for critical/high/moderate/low response windows.',
  ].filter(Boolean)

  return {
    summary,
    markdown: `${markdownLines.join('\n')}\n`,
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2))
  const outputMarkdown = args.get('output-markdown')
  const outputJson = args.get('output-json')
  const allowlist = args.get('allowlist')
  const summaryTitle = args.get('summary-title') ?? 'Dependency Security Signal Summary'
  const workflowContext = args.get('workflow-context') ?? 'unspecified'

  if (!outputMarkdown || !outputJson || !allowlist) {
    throw new Error('--output-markdown, --output-json, and --allowlist are required')
  }

  const { summary, markdown } = await buildSummary({
    backendReport: args.get('backend-report'),
    backendExitCodeFile: args.get('backend-exit-code-file'),
    frontendReport: args.get('frontend-report'),
    frontendExitCodeFile: args.get('frontend-exit-code-file'),
    allowlist,
    outputMarkdown,
    outputJson,
    policyDoc: args.get('policy-doc'),
    summaryTitle,
    workflowContext,
    today: args.get('today'),
  })

  await mkdir(dirname(resolve(outputMarkdown)), { recursive: true })
  await mkdir(dirname(resolve(outputJson)), { recursive: true })
  await writeFile(resolve(outputMarkdown), markdown, 'utf8')
  await writeFile(resolve(outputJson), `${JSON.stringify(summary, null, 2)}\n`, 'utf8')
}

const entryUrl = process.argv[1] ? pathToFileURL(resolve(process.argv[1])) : null
if (entryUrl && import.meta.url === entryUrl.href) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error))
    process.exit(1)
  })
}
