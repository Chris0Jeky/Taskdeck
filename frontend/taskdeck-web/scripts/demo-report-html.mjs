/**
 * demo-report-html.mjs
 *
 * Generates a self-contained static HTML report from demo artifact bundles
 * (run-summary.json, trace.ndjson, screenshots). No external dependencies.
 */

import fs from 'node:fs/promises'
import path from 'node:path'

/**
 * @typedef {object} DemoReportInput
 * @property {object} runSummary - Parsed run-summary.json
 * @property {Array<object>} traceEvents - Parsed NDJSON trace events
 * @property {Array<{name: string, dataUrl: string}>} screenshots - Base64-encoded screenshots
 */

/**
 * Escapes HTML special characters to prevent injection.
 * @param {string} text
 * @returns {string}
 */
export function escapeHtml(text) {
  const value = String(text ?? '')
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;')
}

/**
 * Classifies a trace event into a pass/fail/info status for display.
 * @param {object} event
 * @returns {'pass' | 'fail' | 'skip' | 'info'}
 */
export function classifyEventStatus(event) {
  const type = String(event?.type || '')
  if (type.endsWith('.error')) return 'fail'
  if (type.endsWith('.ok')) return 'pass'
  if (type.endsWith('.skipped')) return 'skip'
  return 'info'
}

/**
 * Extracts step-by-step trace rows for the report.
 * @param {Array<object>} events
 * @returns {Array<{ts: string, type: string, status: string, label: string, detail: string}>}
 */
export function extractTraceSteps(events) {
  return events.map((event) => {
    const type = String(event?.type || 'unknown')
    const status = classifyEventStatus(event)
    const label = event?.stepLabel || event?.stepType || type
    const detail = event?.error || event?.reason || event?.outcome || ''
    return {
      ts: event?.ts || '',
      type,
      status,
      label: String(label),
      detail: String(detail),
    }
  })
}

/**
 * Reads a PNG file and returns a data-URL string, or null on failure.
 * @param {string} filePath
 * @returns {Promise<string | null>}
 */
export async function screenshotToDataUrl(filePath) {
  try {
    const buffer = await fs.readFile(filePath)
    return `data:image/png;base64,${buffer.toString('base64')}`
  } catch {
    return null
  }
}

/**
 * Generates the self-contained HTML report string.
 * @param {DemoReportInput} input
 * @returns {string}
 */
export function generateHtmlReport({ runSummary, traceEvents, screenshots }) {
  const scenario = escapeHtml(runSummary?.scenario || 'unknown')
  const status = runSummary?.status || 'unknown'
  const statusClass = status === 'ok' ? 'status-pass' : 'status-fail'
  const runId = escapeHtml(runSummary?.runId || 'N/A')
  const startedAt = escapeHtml(runSummary?.startedAt || '')
  const endedAt = escapeHtml(runSummary?.endedAt || '')
  const stats = runSummary?.stats || {}

  const steps = extractTraceSteps(traceEvents)

  const stepRows = steps
    .map((step) => {
      const statusBadge =
        step.status === 'pass'
          ? '<span class="badge badge-pass">PASS</span>'
          : step.status === 'fail'
            ? '<span class="badge badge-fail">FAIL</span>'
            : step.status === 'skip'
              ? '<span class="badge badge-skip">SKIP</span>'
              : '<span class="badge badge-info">INFO</span>'

      return (
        `<tr>` +
        `<td class="mono">${escapeHtml(step.ts)}</td>` +
        `<td>${statusBadge}</td>` +
        `<td>${escapeHtml(step.type)}</td>` +
        `<td>${escapeHtml(step.label)}</td>` +
        `<td>${escapeHtml(step.detail)}</td>` +
        `</tr>`
      )
    })
    .join('\n')

  const screenshotHtml =
    screenshots.length === 0
      ? '<p>No screenshots captured.</p>'
      : screenshots
          .map(
            (s) =>
              `<div class="screenshot"><h4>${escapeHtml(s.name)}</h4>` +
              `<img src="${s.dataUrl}" alt="${escapeHtml(s.name)}" /></div>`,
          )
          .join('\n')

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>Taskdeck Demo Report - ${scenario}</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; color: #1a1a2e; background: #f5f5f7; padding: 2rem; }
  .container { max-width: 1100px; margin: 0 auto; }
  h1 { font-size: 1.5rem; margin-bottom: 0.5rem; }
  h2 { font-size: 1.2rem; margin: 1.5rem 0 0.75rem; border-bottom: 2px solid #e0e0e0; padding-bottom: 0.25rem; }
  h3 { font-size: 1rem; margin: 1rem 0 0.5rem; }
  .header { background: #fff; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
  .meta { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.5rem; margin-top: 0.75rem; }
  .meta dt { font-weight: 600; font-size: 0.85rem; color: #666; }
  .meta dd { margin-bottom: 0.5rem; }
  .status-pass { color: #16a34a; }
  .status-fail { color: #dc2626; }
  .badge { display: inline-block; padding: 0.1rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }
  .badge-pass { background: #dcfce7; color: #166534; }
  .badge-fail { background: #fee2e2; color: #991b1b; }
  .badge-skip { background: #fef9c3; color: #854d0e; }
  .badge-info { background: #e0e7ff; color: #3730a3; }
  .section { background: #fff; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
  table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
  th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid #eee; }
  th { background: #f8f9fa; font-weight: 600; position: sticky; top: 0; }
  .mono { font-family: 'SF Mono', Menlo, Consolas, monospace; font-size: 0.8rem; }
  .screenshot img { max-width: 100%; border: 1px solid #ddd; border-radius: 4px; margin-top: 0.5rem; }
  .screenshot { margin-bottom: 1.5rem; }
  .footer { text-align: center; font-size: 0.75rem; color: #999; margin-top: 2rem; }
</style>
</head>
<body>
<div class="container">
  <div class="header">
    <h1>Taskdeck Demo Report</h1>
    <dl class="meta">
      <dt>Scenario</dt><dd>${scenario}</dd>
      <dt>Run ID</dt><dd class="mono">${runId}</dd>
      <dt>Status</dt><dd class="${statusClass}"><strong>${escapeHtml(status.toUpperCase())}</strong></dd>
      <dt>Started</dt><dd>${startedAt}</dd>
      <dt>Ended</dt><dd>${endedAt}</dd>
      <dt>Events</dt><dd>${stats.events ?? 0}</dd>
      <dt>Proposals</dt><dd>${stats.proposals ?? 0}</dd>
      <dt>Captures</dt><dd>${stats.captures ?? 0}</dd>
    </dl>
  </div>

  <div class="section">
    <h2>Step-by-Step Trace</h2>
    <table>
      <thead><tr><th>Timestamp</th><th>Status</th><th>Type</th><th>Label</th><th>Detail</th></tr></thead>
      <tbody>${stepRows || '<tr><td colspan="5">No trace events.</td></tr>'}</tbody>
    </table>
  </div>

  <div class="section">
    <h2>Screenshots</h2>
    ${screenshotHtml}
  </div>

  <div class="footer">Generated by Taskdeck Demo Director</div>
</div>
</body>
</html>`
}

/**
 * Reads an artifact directory and produces a complete HTML report file.
 * @param {string} artifactDir - Path to the demo-artifacts/run-xxx directory
 * @param {string} outputPath - Where to write the HTML file
 */
export async function generateReportFromArtifacts(artifactDir, outputPath) {
  const summaryPath = path.join(artifactDir, 'run-summary.json')
  const tracePath = path.join(artifactDir, 'trace.ndjson')
  const screenshotsDir = path.join(artifactDir, 'screenshots')

  const runSummary = JSON.parse(await fs.readFile(summaryPath, 'utf8'))

  let traceEvents = []
  try {
    const raw = await fs.readFile(tracePath, 'utf8')
    for (const line of raw.split('\n')) {
      const trimmed = line.trim()
      if (!trimmed) continue
      try {
        traceEvents.push(JSON.parse(trimmed))
      } catch {
        // skip malformed lines
      }
    }
  } catch {
    // trace file may not exist
  }

  const screenshots = []
  try {
    const files = await fs.readdir(screenshotsDir)
    const pngs = files.filter((f) => f.toLowerCase().endsWith('.png')).sort()
    for (const file of pngs) {
      const dataUrl = await screenshotToDataUrl(path.join(screenshotsDir, file))
      if (dataUrl) {
        screenshots.push({ name: file, dataUrl })
      }
    }
  } catch {
    // screenshots dir may not exist
  }

  const html = generateHtmlReport({ runSummary, traceEvents, screenshots })
  await fs.mkdir(path.dirname(outputPath), { recursive: true })
  await fs.writeFile(outputPath, html, 'utf8')
  return outputPath
}
