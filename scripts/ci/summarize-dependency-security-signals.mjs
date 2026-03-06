#!/usr/bin/env node

import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, relative, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

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

function createSeverityCounts() {
  return {
    critical: 0,
    high: 0,
    moderate: 0,
    low: 0,
    unknown: 0,
  }
}

function summarizeBackendReport(report, exitCode) {
  const severityCounts = createSeverityCounts()
  const packages = new Map()

  for (const project of report?.projects ?? []) {
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
            advisoryUrls: new Set(),
          }

          packageSummary.projects.add(project.path)

          for (const vulnerability of pkg.vulnerabilities) {
            const severity = normalizeSeverity(vulnerability.severity)
            severityCounts[severity] += 1
            packageSummary.severities.add(severity)
            if (vulnerability.advisoryurl) {
              packageSummary.advisoryUrls.add(vulnerability.advisoryurl)
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
      advisoryUrls: [...pkg.advisoryUrls].sort(),
      projectCount: pkg.projects.size,
    }))
    .sort((left, right) => left.id.localeCompare(right.id))

  return {
    exitCode,
    packageCount: packageList.length,
    severityCounts,
    packages: packageList,
    highOrCriticalCount: severityCounts.high + severityCounts.critical,
    hasFindings: packageList.length > 0,
    parseFailed: false,
  }
}

function summarizeFrontendReport(report, exitCode) {
  const metadataCounts = report?.metadata?.vulnerabilities ?? {}
  const severityCounts = {
    critical: Number(metadataCounts.critical ?? 0),
    high: Number(metadataCounts.high ?? 0),
    moderate: Number(metadataCounts.moderate ?? 0),
    low: Number(metadataCounts.low ?? 0),
    unknown: 0,
  }

  const packages = Object.values(report?.vulnerabilities ?? {})
    .map((entry) => ({
      id: entry.name,
      severity: normalizeSeverity(entry.severity),
      isDirect: Boolean(entry.isDirect),
      fixAvailable: entry.fixAvailable === true || typeof entry.fixAvailable === 'object',
    }))
    .sort((left, right) => left.id.localeCompare(right.id))

  return {
    exitCode,
    packageCount: packages.length,
    severityCounts,
    packages,
    highOrCriticalCount: severityCounts.high + severityCounts.critical,
    hasFindings: Number(metadataCounts.total ?? 0) > 0,
    parseFailed: false,
  }
}

function summarizeScanFailure(exitCode) {
  return {
    exitCode,
    packageCount: 0,
    severityCounts: createSeverityCounts(),
    packages: [],
    highOrCriticalCount: 0,
    hasFindings: false,
    parseFailed: true,
  }
}

function formatSeverityCounts(counts) {
  return `critical ${counts.critical}, high ${counts.high}, moderate ${counts.moderate}, low ${counts.low}, unknown ${counts.unknown}`
}

function formatBackendPackage(pkg) {
  return `${pkg.id}@${pkg.version} (${pkg.severities.join('/')}, projects=${pkg.projectCount})`
}

function formatFrontendPackage(pkg) {
  const fixStatus = pkg.fixAvailable ? 'fix available' : 'no fix'
  const directness = pkg.isDirect ? 'direct' : 'transitive'
  return `${pkg.id} (${pkg.severity}, ${directness}, ${fixStatus})`
}

function toRepoRelative(path) {
  return relative(process.cwd(), resolve(path)).replaceAll('\\', '/')
}

export async function buildSummary(options) {
  const backendExitCode = await readExitCode(options.backendExitCodeFile)
  const frontendExitCode = await readExitCode(options.frontendExitCodeFile)

  let backend
  try {
    backend = summarizeBackendReport(await readJsonIfPresent(options.backendReport), backendExitCode)
  } catch {
    backend = summarizeScanFailure(backendExitCode)
  }

  let frontend
  try {
    frontend = summarizeFrontendReport(await readJsonIfPresent(options.frontendReport), frontendExitCode)
  } catch {
    frontend = summarizeScanFailure(frontendExitCode)
  }

  const summary = {
    summaryTitle: options.summaryTitle,
    workflowContext: options.workflowContext,
    policyDoc: options.policyDoc ? toRepoRelative(options.policyDoc) : null,
    backend,
    frontend,
    totals: {
      highOrCriticalFindings: backend.highOrCriticalCount + frontend.highOrCriticalCount,
      parseFailures: Number(backend.parseFailed) + Number(frontend.parseFailed),
      hasActionableFindings: backend.highOrCriticalCount + frontend.highOrCriticalCount > 0,
    },
  }

  const markdownLines = [
    `## ${options.summaryTitle}`,
    '',
    `- Workflow context: ${options.workflowContext}`,
    options.policyDoc ? `- Policy: \`${toRepoRelative(options.policyDoc)}\`` : null,
    '- Release-blocking threshold: unresolved high/critical dependency findings or scan command failures in an enforcement run.',
    '',
    '### Backend',
    `- Exit code: ${backend.exitCode}`,
    `- Parse status: ${backend.parseFailed ? 'failed' : 'ok'}`,
    `- Vulnerable packages: ${backend.packageCount}`,
    `- Severity counts: ${formatSeverityCounts(backend.severityCounts)}`,
    backend.packages.length > 0 ? `- Top packages: ${backend.packages.slice(0, 5).map(formatBackendPackage).join('; ')}` : '- Top packages: none',
    '',
    '### Frontend',
    `- Exit code: ${frontend.exitCode}`,
    `- Parse status: ${frontend.parseFailed ? 'failed' : 'ok'}`,
    `- Vulnerable packages: ${frontend.packageCount}`,
    `- Severity counts: ${formatSeverityCounts(frontend.severityCounts)}`,
    frontend.packages.length > 0 ? `- Top packages: ${frontend.packages.slice(0, 5).map(formatFrontendPackage).join('; ')}` : '- Top packages: none',
    '',
    '### Triage Guidance',
    '- Owner: maintainers of the touched dependency surface with security review by repository maintainers.',
    '- Required action: remediate, pin/upgrade safely, or document an exception with owner, rationale, compensating controls, and expiry.',
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
  const summaryTitle = args.get('summary-title') ?? 'Dependency Security Signal Summary'
  const workflowContext = args.get('workflow-context') ?? 'unspecified'

  if (!outputMarkdown || !outputJson) {
    throw new Error('--output-markdown and --output-json are required')
  }

  const { summary, markdown } = await buildSummary({
    backendReport: args.get('backend-report'),
    backendExitCodeFile: args.get('backend-exit-code-file'),
    frontendReport: args.get('frontend-report'),
    frontendExitCodeFile: args.get('frontend-exit-code-file'),
    outputMarkdown,
    outputJson,
    policyDoc: args.get('policy-doc'),
    summaryTitle,
    workflowContext,
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
