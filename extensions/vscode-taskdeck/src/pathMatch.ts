import * as path from 'node:path';

// Normalize a filesystem path for comparison. On Windows the filesystem is
// case-insensitive, so paths are lowercased; elsewhere they are compared as-is.
export function normalizeFsPath(fsPath: string): string {
  return process.platform === 'win32'
    ? fsPath.toLowerCase()
    : fsPath;
}

// True when `child` is the same path as, or nested beneath, `parent`. Used to
// match a document against the most specific Git repository root that contains
// it, rather than requiring an exact equality with the workspace folder.
export function isPathWithin(child: string, parent: string): boolean {
  const c = normalizeFsPath(child);
  const p = normalizeFsPath(parent).replace(/[\\/]+$/, '');
  if (c === p) return true;
  return c.startsWith(`${p}/`) || c.startsWith(`${p}${path.sep}`);
}
