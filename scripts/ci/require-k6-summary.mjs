#!/usr/bin/env node

import { readFileSync } from 'node:fs'

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

if (summary === null || typeof summary !== 'object' || Array.isArray(summary)) {
  fail(`Required k6 summary must be a JSON object: ${summaryPath}`)
}

if (summary.metrics === null || typeof summary.metrics !== 'object' || Array.isArray(summary.metrics)) {
  fail(`Required k6 summary must contain a metrics object: ${summaryPath}`)
}

console.log(`k6 summary validated: ${summaryPath}`)
