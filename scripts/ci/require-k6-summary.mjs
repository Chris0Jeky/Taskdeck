#!/usr/bin/env node

import { readFileSync } from 'node:fs'
import { validateK6HardGateSummary } from './k6-summary-contract.mjs'

const summaryPath = process.argv[2]

function fail(message) {
  console.error(`::error::${message}`)
  process.exit(1)
}

if (!summaryPath) {
  fail('Usage: require-k6-summary.mjs <k6-summary.json>')
}

let rawSummary
try {
  rawSummary = readFileSync(summaryPath, 'utf8')
} catch (error) {
  fail(`Required k6 summary is missing or unreadable: ${summaryPath} (${error.message})`)
}

if (!rawSummary.trim()) {
  fail(`Required k6 summary is empty: ${summaryPath}`)
}

let summary
try {
  summary = JSON.parse(rawSummary)
} catch (error) {
  fail(`Required k6 summary is not valid JSON: ${summaryPath} (${error.message})`)
}

const validationError = validateK6HardGateSummary(summary)
if (validationError) {
  fail(`Required k6 summary ${validationError}: ${summaryPath}`)
}

console.log(`k6 summary validated: ${summaryPath}`)
