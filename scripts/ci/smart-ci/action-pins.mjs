#!/usr/bin/env node
// External action pin inventory (ADR-0066 §12, CI-11 #2335).
//
//   node scripts/ci/smart-ci/action-pins.mjs [--workflows .github/workflows] [--json out.json] [--markdown out.md] [--check]
//
// Lists every `uses:` reference in the workflow files, classifies it (local, docker,
// external), and reports whether an external reference is pinned to a full 40-hex commit
// SHA. `--check` exits 1 when any external reference is unpinned (the CI-11 guard; report-only
// until every action is migrated).

import { readdirSync, readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { join } from 'node:path';

const USES_PATTERN = /^\s*-?\s*uses:\s*["']?([^\s"'#]+)["']?\s*(#.*)?$/;

export function inventoryActionPins(files) {
  const entries = [];
  for (const file of files) {
    const lines = String(file.text).split(/\r?\n/);
    lines.forEach((line, index) => {
      const match = USES_PATTERN.exec(line);
      if (!match) return;
      const uses = match[1];
      const comment = match[2] ? match[2].replace(/^#\s*/, '') : '';
      let kind = 'external';
      let action = uses;
      let ref = '';
      if (uses.startsWith('./') || uses.startsWith('.github/')) {
        kind = 'local';
      } else if (uses.startsWith('docker://')) {
        kind = 'docker';
        const at = uses.indexOf('@');
        action = at >= 0 ? uses.slice(0, at) : uses;
        ref = at >= 0 ? uses.slice(at + 1) : '';
      } else {
        const at = uses.lastIndexOf('@');
        action = at >= 0 ? uses.slice(0, at) : uses;
        ref = at >= 0 ? uses.slice(at + 1) : '';
      }
      const pinned = kind === 'local' ? true : kind === 'docker' ? /^sha256:[0-9a-f]{64}$/.test(ref) : /^[0-9a-f]{40}$/.test(ref);
      entries.push({ file: file.path, line: index + 1, uses, kind, action, ref, pinned, comment });
    });
  }
  entries.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line);
  const external = entries.filter((entry) => entry.kind !== 'local');
  const unpinned = external.filter((entry) => !entry.pinned);
  const byAction = {};
  for (const entry of external) {
    byAction[entry.action] ??= { references: 0, pinned: 0, refs: new Set() };
    byAction[entry.action].references += 1;
    if (entry.pinned) byAction[entry.action].pinned += 1;
    byAction[entry.action].refs.add(entry.ref);
  }
  return {
    entries,
    summary: {
      files: files.length,
      references: entries.length,
      external: external.length,
      pinned: external.length - unpinned.length,
      unpinned: unpinned.length,
      distinctExternalActions: Object.keys(byAction).length,
    },
    byAction: Object.fromEntries(Object.entries(byAction).sort().map(([action, stat]) => [action, { references: stat.references, pinned: stat.pinned, refs: [...stat.refs].sort() }])),
  };
}

export function renderPinsMarkdown(inventory) {
  const lines = [];
  lines.push('### External action pin inventory (CI-11)');
  lines.push('');
  lines.push(`${inventory.summary.external} external references across ${inventory.summary.files} workflow files · **${inventory.summary.pinned} pinned to a full SHA**, **${inventory.summary.unpinned} unpinned** · ${inventory.summary.distinctExternalActions} distinct actions`);
  lines.push('');
  lines.push('| Action | references | pinned | refs in use |');
  lines.push('| --- | ---: | ---: | --- |');
  for (const [action, stat] of Object.entries(inventory.byAction)) lines.push(`| ${action} | ${stat.references} | ${stat.pinned} | ${stat.refs.map((ref) => `\`${ref}\``).join(', ')} |`);
  lines.push('');
  return `${lines.join('\n')}\n`;
}

function parseArgs(argv) {
  const args = { workflows: '.github/workflows', json: null, markdown: null, check: false };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = () => argv[++index];
    switch (arg) {
      case '--workflows': args.workflows = next(); break;
      case '--json': args.json = next(); break;
      case '--markdown': args.markdown = next(); break;
      case '--check': args.check = true; break;
      case '--help': console.log('usage: action-pins.mjs [--workflows DIR] [--json FILE] [--markdown FILE] [--check]'); process.exit(0); break;
      default: throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const files = readdirSync(args.workflows)
    .filter((name) => /\.ya?ml$/.test(name))
    .sort()
    .map((name) => ({ path: join(args.workflows, name).replace(/\\/g, '/'), text: readFileSync(join(args.workflows, name), 'utf8') }));
  const inventory = inventoryActionPins(files);
  const markdown = renderPinsMarkdown(inventory);
  if (args.json) writeFileSync(args.json, `${JSON.stringify(inventory, null, 2)}\n`);
  if (args.markdown) appendFileSync(args.markdown, markdown);
  process.stdout.write(markdown);
  if (args.check && inventory.summary.unpinned > 0) {
    for (const entry of inventory.entries.filter((candidate) => candidate.kind !== 'local' && !candidate.pinned)) console.error(`::error file=${entry.file},line=${entry.line}::unpinned action reference ${entry.uses}`);
    process.exit(1);
  }
}

if (process.argv[1] && /action-pins\.mjs$/.test(process.argv[1])) main();
