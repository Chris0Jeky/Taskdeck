import * as assert from 'node:assert';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

type ExtensionManifest = {
  activationEvents?: string[];
}

const manifestPath = resolve(__dirname, '../package.json');
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8')) as ExtensionManifest;

assert.ok(
  manifest.activationEvents?.includes('onStartupFinished'),
  'extension must activate on startup so the Taskdeck status-bar entry is visible before any command runs',
);
