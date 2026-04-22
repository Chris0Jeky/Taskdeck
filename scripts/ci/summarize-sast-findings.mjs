#!/usr/bin/env node

/**
 * summarize-sast-findings.mjs
 *
 * Reads Semgrep JSON output and produces a Markdown summary for GitHub CI
 * step summaries. Supports enforcement mode (exit 1 on high/critical).
 *
 * Usage:
 *   node scripts/ci/summarize-sast-findings.mjs \
 *     --input <semgrep-json-file> \
 *     --exit-code-file <file-with-semgrep-exit-code> \
 *     --summary-title "SAST Scan Summary" \
 *     --workflow-context "ci-extended" \
 *     --output-markdown <output.md> \
 *     --output-json <output.json> \
 *     [--enforce]
 */

import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { resolve } from "node:path";

// ---------------------------------------------------------------------------
// CLI argument parsing
// ---------------------------------------------------------------------------
const args = process.argv.slice(2);

function getArg(name, fallback = undefined) {
  const idx = args.indexOf(`--${name}`);
  if (idx === -1 || idx + 1 >= args.length) return fallback;
  return args[idx + 1];
}

const hasFlag = (name) => args.includes(`--${name}`);

const inputFile = getArg("input");
const exitCodeFile = getArg("exit-code-file");
const summaryTitle = getArg("summary-title", "SAST Scan Summary");
const workflowContext = getArg("workflow-context", "unspecified");
const outputMarkdown = getArg("output-markdown");
const outputJson = getArg("output-json");
const enforce = hasFlag("enforce");

if (!inputFile) {
  console.error("Error: --input is required");
  process.exit(2);
}

// ---------------------------------------------------------------------------
// Read inputs
// ---------------------------------------------------------------------------
let semgrepResults;
let scanExitCode = 0;

try {
  const raw = readFileSync(resolve(inputFile), "utf8");
  semgrepResults = JSON.parse(raw);
} catch (err) {
  console.error(`Failed to read/parse Semgrep output: ${err.message}`);
  // If the file doesn't exist or is invalid, create an empty result
  semgrepResults = { results: [], errors: [] };
}

if (exitCodeFile && existsSync(resolve(exitCodeFile))) {
  scanExitCode = parseInt(
    readFileSync(resolve(exitCodeFile), "utf8").trim(),
    10,
  );
  if (isNaN(scanExitCode)) scanExitCode = 0;
}

// ---------------------------------------------------------------------------
// Categorize findings
// ---------------------------------------------------------------------------
const findings = semgrepResults.results || [];
const errors = semgrepResults.errors || [];

const severityCounts = { ERROR: 0, WARNING: 0, INFO: 0 };
const findingsByRule = {};
const findingsBySeverity = { ERROR: [], WARNING: [], INFO: [] };

for (const f of findings) {
  const sev = (f.extra?.severity || "INFO").toUpperCase();
  const ruleId = f.check_id || "unknown";

  if (!severityCounts[sev]) severityCounts[sev] = 0;
  severityCounts[sev]++;

  if (!findingsByRule[ruleId]) findingsByRule[ruleId] = [];
  findingsByRule[ruleId].push(f);

  if (!findingsBySeverity[sev]) findingsBySeverity[sev] = [];
  findingsBySeverity[sev].push(f);
}

const totalFindings = findings.length;
const hasHighCritical = severityCounts.ERROR > 0;
const hasEnforcementFailures = enforce && hasHighCritical;

// ---------------------------------------------------------------------------
// Generate Markdown summary
// ---------------------------------------------------------------------------
const lines = [];

lines.push(`## ${summaryTitle}`);
lines.push("");
lines.push(
  `> Workflow: \`${workflowContext}\` | Scan exit code: \`${scanExitCode}\` | Enforcement: ${enforce ? "**enabled**" : "advisory (disabled)"}`,
);
lines.push("");

// Overall status
if (totalFindings === 0 && errors.length === 0) {
  lines.push(
    ":white_check_mark: **No SAST findings.** The codebase passed all configured rules.",
  );
} else {
  if (hasEnforcementFailures) {
    lines.push(
      ":x: **Enforcement failure** -- high-severity (ERROR) findings detected.",
    );
  } else if (hasHighCritical) {
    lines.push(
      ":warning: **Advisory** -- high-severity findings detected but enforcement is disabled.",
    );
  } else if (totalFindings > 0) {
    lines.push(
      ":information_source: **Advisory** -- lower-severity findings detected.",
    );
  }
}

lines.push("");

// Summary table
lines.push("### Finding Summary");
lines.push("");
lines.push("| Severity | Count |");
lines.push("|----------|-------|");
lines.push(`| :red_circle: ERROR (high) | ${severityCounts.ERROR} |`);
lines.push(`| :orange_circle: WARNING (medium) | ${severityCounts.WARNING} |`);
lines.push(`| :blue_circle: INFO (low) | ${severityCounts.INFO} |`);
lines.push(`| **Total** | **${totalFindings}** |`);
lines.push("");

// Findings by rule
if (totalFindings > 0) {
  lines.push("### Findings by Rule");
  lines.push("");

  for (const [ruleId, ruleFindings] of Object.entries(findingsByRule).sort(
    (a, b) => b[1].length - a[1].length,
  )) {
    const sev = (ruleFindings[0]?.extra?.severity || "INFO").toUpperCase();
    lines.push(
      `<details><summary><b>${ruleId}</b> (${sev}) -- ${ruleFindings.length} finding(s)</summary>`,
    );
    lines.push("");
    lines.push("| File | Line | Message |");
    lines.push("|------|------|---------|");

    // Limit to first 25 per rule to avoid massive summaries
    const displayFindings = ruleFindings.slice(0, 25);
    for (const f of displayFindings) {
      const file = f.path || "unknown";
      const line = f.start?.line || "?";
      const msg = (f.extra?.message || "").replace(/\n/g, " ").slice(0, 200);
      lines.push(`| \`${file}\` | ${line} | ${msg} |`);
    }

    if (ruleFindings.length > 25) {
      lines.push(
        `| ... | ... | _${ruleFindings.length - 25} more findings omitted_ |`,
      );
    }

    lines.push("");
    lines.push("</details>");
    lines.push("");
  }
}

// Errors
if (errors.length > 0) {
  lines.push("### Scan Errors");
  lines.push("");
  lines.push(
    "The following errors occurred during scanning (these may indicate parse failures or rule issues):",
  );
  lines.push("");
  for (const e of errors.slice(0, 10)) {
    const msg =
      typeof e === "string"
        ? e
        : e.message || e.long_msg || JSON.stringify(e);
    lines.push(`- ${msg}`);
  }
  if (errors.length > 10) {
    lines.push(`- _${errors.length - 10} more errors omitted_`);
  }
  lines.push("");
}

// Custom rules reference
lines.push("### Rule Sources");
lines.push("");
lines.push("- **Semgrep registry**: `p/csharp`, `p/typescript`, `p/jwt`");
lines.push("- **Taskdeck custom**: `.semgrep/taskdeck-csharp.yml`, `.semgrep/taskdeck-typescript.yml`");
lines.push("");

const markdown = lines.join("\n");

// ---------------------------------------------------------------------------
// JSON summary
// ---------------------------------------------------------------------------
const jsonSummary = {
  title: summaryTitle,
  workflowContext,
  scanExitCode,
  enforce,
  totals: {
    findings: totalFindings,
    errors: errors.length,
    bySeverity: severityCounts,
    hasHighCritical,
    hasEnforcementFailures,
  },
  ruleBreakdown: Object.fromEntries(
    Object.entries(findingsByRule).map(([ruleId, ruleFindings]) => [
      ruleId,
      {
        count: ruleFindings.length,
        severity: (ruleFindings[0]?.extra?.severity || "INFO").toUpperCase(),
      },
    ]),
  ),
};

// ---------------------------------------------------------------------------
// Write outputs
// ---------------------------------------------------------------------------
if (outputMarkdown) {
  writeFileSync(resolve(outputMarkdown), markdown, "utf8");
  console.log(`Markdown summary written to ${outputMarkdown}`);
}

if (outputJson) {
  writeFileSync(resolve(outputJson), JSON.stringify(jsonSummary, null, 2), "utf8");
  console.log(`JSON summary written to ${outputJson}`);
}

// Print markdown to stdout for piping into GITHUB_STEP_SUMMARY
console.log(markdown);

// Exit with failure if enforcement is active and high-severity findings exist
if (hasEnforcementFailures) {
  console.error(
    `Enforcement active: ${severityCounts.ERROR} ERROR-level finding(s) detected. Failing the step.`,
  );
  process.exit(1);
}
