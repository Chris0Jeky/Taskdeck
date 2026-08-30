#!/usr/bin/env node

// check-bundle-size.mjs
// Checks frontend build output against size budgets.
// Emits GitHub Actions annotations (::warning / ::error) when limits are breached.
//
// Usage:
//   node scripts/ci/check-bundle-size.mjs [--dist <path>] [--fail-on-error]
//
// Environment variables (override defaults):
//   BUNDLE_MAX_ENTRY_KB       - max size (KB) for the main entry chunk (default: 150)
//   BUNDLE_MAX_SINGLE_KB      - max size (KB) for any single JS chunk (default: 250)
//   BUNDLE_MAX_EAGER_JS_KB    - max eager-graph JS size (KB): entry + modulepreload (default: 1250, ERROR gate)
//   BUNDLE_MAX_TOTAL_JS_KB    - total emitted JS size (KB) across all chunks (default: 1280, WARNING only)
//   BUNDLE_WARN_ENTRY_KB      - warning threshold (KB) for main entry chunk (default: 120)
//   BUNDLE_WARN_SINGLE_KB     - warning threshold (KB) for any single JS chunk (default: 200)
//   BUNDLE_WARN_EAGER_JS_KB   - warning threshold (KB) for eager-graph JS (default: 1000)
//   BUNDLE_WARN_TOTAL_JS_KB   - warning threshold (KB) for total JS size (default: 1000)

import { readdirSync, readFileSync, statSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";

const args = process.argv.slice(2);

function getArg(name, fallback) {
  const idx = args.indexOf(name);
  return idx !== -1 && args[idx + 1] ? args[idx + 1] : fallback;
}

const distDir = getArg("--dist", "frontend/taskdeck-web/dist");
const failOnError = args.includes("--fail-on-error");
const outputJson = getArg("--output-json", null);

// Thresholds (KB)
const MAX_ENTRY_KB = Number(process.env.BUNDLE_MAX_ENTRY_KB || "150");
const MAX_SINGLE_KB = Number(process.env.BUNDLE_MAX_SINGLE_KB || "250");
// #1858 RULED (maintainer, 2026-08-23): the HARD gate measures the EAGER
// GRAPH — the entry chunk plus everything index.html modulepreloads, i.e. the
// JS a first paint actually downloads — at the 1250 KB budget that used to
// apply to total JS. Lazy-loaded chunks (route views, the code-split it/es
// locale catalogs) are what code-splitting is FOR and do not spend this
// budget, which is what lets i18n rollout step 3 proceed without growing the
// gated number (~0 KB eager per surface once catalogs are lazy).
//
// History of the number: 1200 -> 1250 on 2026-08-19 (#1770, i18n catalogs);
// 1250 -> 1280 on 2026-08-22 as an emergency total-JS stopgap after the
// dogfooding fix wave tipped main red at 1251.08 KB. That stopgap is retired
// by this ruling: total emitted JS remains REPORTED and WARNS (it is real
// telemetry — bytes users eventually download), but no longer fails the lane.
// The nightly Performance Regression Gate's defaults in
// reusable-performance-regression-gate.yml are kept in step — if the two
// drift, the required lane and that gate disagree about identical bytes.
const MAX_EAGER_JS_KB = Number(process.env.BUNDLE_MAX_EAGER_JS_KB || "1250");
const WARN_EAGER_JS_KB = Number(process.env.BUNDLE_WARN_EAGER_JS_KB || "1000");
const MAX_TOTAL_JS_KB = Number(process.env.BUNDLE_MAX_TOTAL_JS_KB || "1280");
const WARN_ENTRY_KB = Number(process.env.BUNDLE_WARN_ENTRY_KB || "120");
const WARN_SINGLE_KB = Number(process.env.BUNDLE_WARN_SINGLE_KB || "200");
const WARN_TOTAL_JS_KB = Number(process.env.BUNDLE_WARN_TOTAL_JS_KB || "1000");

function collectJsFiles(dir) {
  const results = [];
  try {
    const entries = readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = join(dir, entry.name);
      if (entry.isDirectory()) {
        results.push(...collectJsFiles(fullPath));
      } else if (entry.name.endsWith(".js")) {
        const stat = statSync(fullPath);
        results.push({ path: fullPath, name: entry.name, sizeBytes: stat.size });
      }
    }
  } catch {
    // directory may not exist
  }
  return results;
}

function isEntryChunk(name) {
  // Vite names the main entry chunk as index-<hash>.js
  // Hash uses Base64url alphabet: A-Z, a-z, 0-9, underscore, hyphen
  return /^index-[A-Za-z0-9_-]+\.js$/.test(name);
}

function formatKB(bytes) {
  return (bytes / 1024).toFixed(2);
}

// --- Main ---

const assetsDir = join(distDir, "assets");
const jsFiles = collectJsFiles(assetsDir);

if (jsFiles.length === 0) {
  console.error(`No JS files found in ${assetsDir}. Did the build run?`);
  process.exit(1);
}

const totalBytes = jsFiles.reduce((sum, f) => sum + f.sizeBytes, 0);
const totalKB = totalBytes / 1024;

const entryFiles = jsFiles.filter((f) => isEntryChunk(f.name));
const entryChunk = entryFiles.length > 0 ? entryFiles[0] : null;
const entryKB = entryChunk ? entryChunk.sizeBytes / 1024 : 0;

const largestFile = jsFiles.reduce((max, f) =>
  f.sizeBytes > max.sizeBytes ? f : max
);
const largestKB = largestFile.sizeBytes / 1024;

// Eager graph (#1858): the JS index.html actually loads up front — <script src>
// plus <link rel="modulepreload"> (Vite emits one per static import of the
// entry). Route views and the code-split locale catalogs are dynamic imports,
// appear in neither list, and are deliberately outside this gate.
function collectEagerNames(dir) {
  let html;
  try {
    html = readFileSync(join(dir, "index.html"), "utf8");
  } catch {
    return null; // no index.html — gate falls back below with an explicit error
  }
  const names = new Set();
  for (const m of html.matchAll(/<script\b[^>]*\bsrc="([^"]+\.js)"/g)) {
    names.add(m[1].split("/").pop());
  }
  for (const m of html.matchAll(/<link\b[^>]*\brel="modulepreload"[^>]*\bhref="([^"]+\.js)"/g)) {
    names.add(m[1].split("/").pop());
  }
  // Attribute order can vary; also catch href-before-rel.
  for (const m of html.matchAll(/<link\b[^>]*\bhref="([^"]+\.js)"[^>]*\brel="modulepreload"/g)) {
    names.add(m[1].split("/").pop());
  }
  return names;
}

const eagerNames = collectEagerNames(distDir);
const eagerFiles = eagerNames ? jsFiles.filter((f) => eagerNames.has(f.name)) : [];
const eagerBytes = eagerFiles.reduce((sum, f) => sum + f.sizeBytes, 0);
const eagerKB = eagerBytes / 1024;

// Sorted by size descending for the report
const sorted = [...jsFiles].sort((a, b) => b.sizeBytes - a.sizeBytes);

console.log("=== Frontend Bundle Size Report ===\n");
console.log("Top 10 JS chunks by size:");
for (const f of sorted.slice(0, 10)) {
  const marker = isEntryChunk(f.name) ? " [ENTRY]" : "";
  console.log(`  ${formatKB(f.sizeBytes).padStart(9)} KB  ${f.name}${marker}`);
}
console.log("");
console.log(`Total JS chunks: ${jsFiles.length}`);
console.log(`Total JS size:   ${formatKB(totalBytes)} KB`);
if (eagerNames) {
  console.log(`Eager JS size:   ${formatKB(eagerBytes)} KB (${eagerFiles.length} chunks: entry + modulepreload)`);
}
if (entryChunk) {
  console.log(`Entry chunk:     ${formatKB(entryChunk.sizeBytes)} KB (${entryChunk.name})`);
}
console.log(`Largest chunk:   ${formatKB(largestFile.sizeBytes)} KB (${largestFile.name})`);
console.log("");

let hasError = false;
let hasWarning = false;
const violations = [];

// Check entry chunk
if (entryChunk) {
  if (entryKB > MAX_ENTRY_KB) {
    const msg = `Entry chunk ${entryChunk.name} is ${formatKB(entryChunk.sizeBytes)} KB, exceeds limit of ${MAX_ENTRY_KB} KB`;
    console.log(`::error::${msg}`);
    violations.push({ level: "error", metric: "entry_chunk_kb", value: entryKB, limit: MAX_ENTRY_KB, message: msg });
    hasError = true;
  } else if (entryKB > WARN_ENTRY_KB) {
    const msg = `Entry chunk ${entryChunk.name} is ${formatKB(entryChunk.sizeBytes)} KB, approaching limit of ${MAX_ENTRY_KB} KB (warn at ${WARN_ENTRY_KB} KB)`;
    console.log(`::warning::${msg}`);
    violations.push({ level: "warning", metric: "entry_chunk_kb", value: entryKB, limit: WARN_ENTRY_KB, message: msg });
    hasWarning = true;
  }
}

// Check largest single chunk
if (largestKB > MAX_SINGLE_KB) {
  const msg = `Chunk ${largestFile.name} is ${formatKB(largestFile.sizeBytes)} KB, exceeds single-chunk limit of ${MAX_SINGLE_KB} KB`;
  console.log(`::error::${msg}`);
  violations.push({ level: "error", metric: "single_chunk_kb", value: largestKB, limit: MAX_SINGLE_KB, message: msg });
  hasError = true;
} else if (largestKB > WARN_SINGLE_KB) {
  const msg = `Chunk ${largestFile.name} is ${formatKB(largestFile.sizeBytes)} KB, approaching single-chunk limit of ${MAX_SINGLE_KB} KB (warn at ${WARN_SINGLE_KB} KB)`;
  console.log(`::warning::${msg}`);
  violations.push({ level: "warning", metric: "single_chunk_kb", value: largestKB, limit: WARN_SINGLE_KB, message: msg });
  hasWarning = true;
}

// Check eager-graph JS size — THE budget gate per the #1858 ruling.
if (eagerNames === null) {
  const msg = `Cannot measure the eager graph: ${join(distDir, "index.html")} is missing or unreadable`;
  console.log(`::error::${msg}`);
  violations.push({ level: "error", metric: "eager_js_kb", value: null, limit: MAX_EAGER_JS_KB, message: msg });
  hasError = true;
} else if (eagerKB > MAX_EAGER_JS_KB) {
  const msg = `Eager JS (entry + modulepreload) is ${formatKB(eagerBytes)} KB, exceeds limit of ${MAX_EAGER_JS_KB} KB`;
  console.log(`::error::${msg}`);
  violations.push({ level: "error", metric: "eager_js_kb", value: eagerKB, limit: MAX_EAGER_JS_KB, message: msg });
  hasError = true;
} else if (eagerKB > WARN_EAGER_JS_KB) {
  const msg = `Eager JS (entry + modulepreload) is ${formatKB(eagerBytes)} KB, approaching limit of ${MAX_EAGER_JS_KB} KB (warn at ${WARN_EAGER_JS_KB} KB)`;
  console.log(`::warning::${msg}`);
  violations.push({ level: "warning", metric: "eager_js_kb", value: eagerKB, limit: WARN_EAGER_JS_KB, message: msg });
  hasWarning = true;
}

// Total emitted JS: telemetry + warning only since the #1858 ruling — code-split
// bytes are still real downloads eventually, but they no longer fail the lane.
if (totalKB > MAX_TOTAL_JS_KB) {
  const msg = `Total JS size is ${formatKB(totalBytes)} KB, above the report ceiling of ${MAX_TOTAL_JS_KB} KB (warning only — the hard gate is eager JS)`;
  console.log(`::warning::${msg}`);
  violations.push({ level: "warning", metric: "total_js_kb", value: totalKB, limit: MAX_TOTAL_JS_KB, message: msg });
  hasWarning = true;
} else if (totalKB > WARN_TOTAL_JS_KB) {
  const msg = `Total JS size is ${formatKB(totalBytes)} KB, approaching limit of ${MAX_TOTAL_JS_KB} KB (warn at ${WARN_TOTAL_JS_KB} KB)`;
  console.log(`::warning::${msg}`);
  violations.push({ level: "warning", metric: "total_js_kb", value: totalKB, limit: WARN_TOTAL_JS_KB, message: msg });
  hasWarning = true;
}

// Summary
if (!hasError && !hasWarning) {
  console.log("All bundle size checks passed.");
}

// Write JSON report if requested
if (outputJson) {
  const report = {
    timestamp: new Date().toISOString(),
    totalJsKB: Math.round(totalKB * 100) / 100,
    eagerJsKB: eagerNames ? Math.round(eagerKB * 100) / 100 : null,
    eagerChunkCount: eagerNames ? eagerFiles.length : null,
    entryChunkKB: entryChunk ? Math.round(entryKB * 100) / 100 : null,
    largestChunkKB: Math.round(largestKB * 100) / 100,
    largestChunkName: largestFile.name,
    chunkCount: jsFiles.length,
    violations,
    thresholds: {
      maxEntryKB: MAX_ENTRY_KB,
      maxSingleKB: MAX_SINGLE_KB,
      maxEagerJsKB: MAX_EAGER_JS_KB,
      maxTotalJsKB: MAX_TOTAL_JS_KB,
      warnEagerJsKB: WARN_EAGER_JS_KB,
      warnEntryKB: WARN_ENTRY_KB,
      warnSingleKB: WARN_SINGLE_KB,
      warnTotalJsKB: WARN_TOTAL_JS_KB,
    },
    chunks: sorted.map((f) => ({
      name: f.name,
      sizeKB: Math.round((f.sizeBytes / 1024) * 100) / 100,
      isEntry: isEntryChunk(f.name),
    })),
  };

  const dir = dirname(outputJson);
  if (dir && dir !== ".") {
    mkdirSync(dir, { recursive: true });
  }
  writeFileSync(outputJson, JSON.stringify(report, null, 2));
  console.log(`\nBundle size report written to ${outputJson}`);
}

if (hasError && failOnError) {
  console.log("\nBundle size check FAILED (--fail-on-error is set).");
  process.exit(1);
}
