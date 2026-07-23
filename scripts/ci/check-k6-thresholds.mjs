#!/usr/bin/env node

// check-k6-thresholds.mjs
// Parses a k6 --summary-export JSON file and emits GitHub Actions annotations
// for threshold violations and near-threshold warnings.
//
// Usage:
//   node scripts/ci/check-k6-thresholds.mjs <k6-summary.json> [--fail-on-breach] [--output-json <path>]
//
// k6 already exits non-zero when thresholds breach. This script adds:
//   1. Human-readable CI summary with ::warning / ::error annotations
//   2. Near-threshold warnings (within 20% of limit) even when not breached
//   3. Optional JSON report for historical tracking

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
import {
  readK6MetricValue,
  readK6ThresholdOk,
  validateK6HardGateSummary,
} from "./k6-summary-contract.mjs";

const args = process.argv.slice(2);
const failOnBreach = args.includes("--fail-on-breach");
const outputJsonIdx = args.indexOf("--output-json");
const outputJson = (outputJsonIdx !== -1 && args[outputJsonIdx + 1]) ? args[outputJsonIdx + 1] : null;

// Collect positional args by skipping flags and their values
const flagsWithValues = new Set(["--output-json"]);
const positionalArgs = [];
for (let i = 0; i < args.length; i++) {
  if (args[i].startsWith("--")) {
    if (flagsWithValues.has(args[i])) i++; // skip the flag's value
    continue;
  }
  positionalArgs.push(args[i]);
}
const summaryPath = positionalArgs[0] || null;

if (!summaryPath) {
  console.error("Usage: check-k6-thresholds.mjs <k6-summary.json> [--fail-on-breach] [--output-json <path>]");
  process.exit(1);
}

let summary;
try {
  summary = JSON.parse(readFileSync(summaryPath, "utf-8"));
} catch (err) {
  console.error(`Failed to read k6 summary: ${err.message}`);
  process.exit(1);
}

const validationError = validateK6HardGateSummary(summary);
if (validationError) {
  console.error(`Invalid k6 hard-gate summary: ${validationError}`);
  process.exit(1);
}

const metrics = summary.metrics;
const thresholds = {};
let hasBreaches = false;
let hasWarnings = false;
const findings = [];

function formatNumber(value) {
  return Number.isFinite(value) ? value.toFixed(2) : "n/a";
}

console.log("=== k6 Performance Threshold Report ===\n");

// Parse threshold results
for (const [metricName, metricData] of Object.entries(metrics)) {
  if (!metricData.thresholds) continue;

  for (const [thresholdExpr, thresholdResult] of Object.entries(metricData.thresholds)) {
    const ok = readK6ThresholdOk(thresholdResult);
    thresholds[`${metricName}: ${thresholdExpr}`] = ok;

    if (!ok) {
      const msg = `k6 threshold breached: ${metricName} ${thresholdExpr}`;
      console.log(`::error::${msg}`);
      findings.push({ level: "error", metric: metricName, threshold: thresholdExpr, ok: false, message: msg });
      hasBreaches = true;
    }
  }
}

// Report key metrics with values
const keyMetrics = [
  { name: "http_req_duration", label: "HTTP request duration" },
  { name: "http_req_failed", label: "HTTP request failure rate" },
  { name: "checks", label: "Check pass rate" },
];

console.log("Key metric values:");
for (const km of keyMetrics) {
  const m = metrics[km.name];
  if (!m) continue;

  const avg = readK6MetricValue(m, "avg");
  const rate = readK6MetricValue(m, "rate");
  if (avg !== undefined) {
    // Duration-type metric
    console.log(`  ${km.label}:`);
    console.log(`    avg=${formatNumber(avg)}ms  med=${formatNumber(readK6MetricValue(m, "med"))}ms  p90=${formatNumber(readK6MetricValue(m, "p(90)"))}ms  p95=${formatNumber(readK6MetricValue(m, "p(95)"))}ms  p99=${formatNumber(readK6MetricValue(m, "p(99)"))}ms  max=${formatNumber(readK6MetricValue(m, "max"))}ms`);
  } else if (rate !== undefined) {
    // Rate-type metric
    console.log(`  ${km.label}: ${(rate * 100).toFixed(2)}%`);
  }
}

// Check for near-threshold conditions and aspirational targets
const p95Limit = 2000; // ms -- hard gate (issue #872)
const p95Aspirational = 1200; // ms -- aspirational target (warning only)
const boardWriteP95Capacity = 2000; // ms -- measured SQLite capacity at 20 VUs (issue #1358)
const boardWriteP95Limit = 4500; // ms -- tail threshold calibrated to same-code nightly variance (issue #1445)
const errorRateLimit = 0.01; // 1%
const nearThresholdRatio = 0.80; // warn at 80% of limit

const httpDuration = metrics["http_req_duration"];
const httpDurationP95 = readK6MetricValue(httpDuration, "p(95)");
const httpDurationP99 = readK6MetricValue(httpDuration, "p(99)");
const httpDurationAvg = readK6MetricValue(httpDuration, "avg");
if (httpDurationP95 !== undefined) {
  const p95 = httpDurationP95;
  if (p95 >= p95Limit) {
    const msg = `HTTP p95 latency is ${p95.toFixed(2)}ms, exceeds ${p95Limit}ms limit`;
    console.log(`\n::error::${msg}`);
    findings.push({ level: "error", metric: "http_req_duration_p95", value: p95, limit: p95Limit, message: msg });
    hasBreaches = true;
  } else if (p95 > p95Aspirational) {
    const msg = `HTTP p95 latency is ${p95.toFixed(2)}ms, exceeds aspirational target of ${p95Aspirational}ms (hard limit: ${p95Limit}ms)`;
    console.log(`\n::warning::${msg}`);
    findings.push({ level: "warning", metric: "http_req_duration_p95_aspirational", value: p95, limit: p95Aspirational, message: msg });
    hasWarnings = true;
  } else if (p95 > p95Limit * nearThresholdRatio) {
    const msg = `HTTP p95 latency is ${p95.toFixed(2)}ms, approaching ${p95Limit}ms limit (${((p95 / p95Limit) * 100).toFixed(0)}%)`;
    console.log(`\n::warning::${msg}`);
    findings.push({ level: "warning", metric: "http_req_duration_p95", value: p95, limit: p95Limit, message: msg });
    hasWarnings = true;
  }
}

const boardWriteDuration = metrics["http_req_duration{workload:board-write}"];
const boardWriteDurationP95 = readK6MetricValue(boardWriteDuration, "p(95)");
if (boardWriteDurationP95 !== undefined) {
  const p95 = boardWriteDurationP95;
  if (p95 >= boardWriteP95Limit) {
    const msg = `Board-write p95 latency is ${p95.toFixed(2)}ms, exceeds ${boardWriteP95Limit}ms hard gate (measured SQLite capacity: ${boardWriteP95Capacity}ms, gate calibrated to nightly tail variance -- see #1445)`;
    console.log(`\n::error::${msg}`);
    findings.push({ level: "error", metric: "board_write_p95", value: p95, limit: boardWriteP95Limit, message: msg });
    hasBreaches = true;
  } else if (p95 >= boardWriteP95Capacity) {
    const msg = `Board-write p95 latency is ${p95.toFixed(2)}ms, at or above measured ${boardWriteP95Capacity}ms SQLite capacity (hard gate: ${boardWriteP95Limit}ms, calibrated to nightly tail variance -- see #1445)`;
    console.log(`\n::warning::${msg}`);
    findings.push({ level: "warning", metric: "board_write_p95_capacity", value: p95, limit: boardWriteP95Capacity, message: msg });
    hasWarnings = true;
  }
}

const httpFailed = metrics["http_req_failed"];
const httpFailedRate = readK6MetricValue(httpFailed, "rate");
if (httpFailedRate !== undefined) {
  const rate = httpFailedRate;
  if (rate >= errorRateLimit) {
    const msg = `HTTP error rate is ${(rate * 100).toFixed(2)}%, exceeds ${(errorRateLimit * 100).toFixed(2)}% limit`;
    console.log(`\n::error::${msg}`);
    findings.push({ level: "error", metric: "http_req_failed_rate", value: rate, limit: errorRateLimit, message: msg });
    hasBreaches = true;
  } else if (rate > errorRateLimit * nearThresholdRatio) {
    const msg = `HTTP error rate is ${(rate * 100).toFixed(2)}%, approaching ${(errorRateLimit * 100).toFixed(2)}% limit (${((rate / errorRateLimit) * 100).toFixed(0)}%)`;
    console.log(`\n::warning::${msg}`);
    findings.push({ level: "warning", metric: "http_req_failed_rate", value: rate, limit: errorRateLimit, message: msg });
    hasWarnings = true;
  }
}

console.log("");
if (!hasBreaches && !hasWarnings) {
  console.log("All k6 performance thresholds passed with comfortable margins.");
} else if (hasBreaches) {
  console.log("PERFORMANCE REGRESSION DETECTED: one or more thresholds breached.");
} else {
  console.log("Thresholds passed but some metrics are approaching limits.");
}

// Write JSON report if requested
if (outputJson) {
  const report = {
    timestamp: new Date().toISOString(),
    metrics: {
      httpReqDurationP95: httpDurationP95 ?? null,
      httpReqDurationP99: httpDurationP99 ?? null,
      httpReqDurationAvg: httpDurationAvg ?? null,
      httpReqFailedRate: httpFailedRate ?? null,
      boardWriteDurationP95: boardWriteDurationP95 ?? null,
    },
    thresholds,
    findings,
    hasBreaches,
    hasWarnings,
  };

  const dir = dirname(outputJson);
  if (dir && dir !== ".") {
    mkdirSync(dir, { recursive: true });
  }
  writeFileSync(outputJson, JSON.stringify(report, null, 2));
  console.log(`\nk6 threshold report written to ${outputJson}`);
}

if (hasBreaches && failOnBreach) {
  process.exit(1);
}
