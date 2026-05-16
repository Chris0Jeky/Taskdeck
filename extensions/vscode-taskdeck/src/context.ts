import * as vscode from 'vscode';
import { execSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import * as path from 'node:path';

export interface WorkspaceContext {
  relativePath: string | null;
  language: string;
  lineRange: string | null;
  gitRemoteHash: string | null;
}

export async function getWorkspaceContext(editor: vscode.TextEditor): Promise<WorkspaceContext> {
  const doc = editor.document;
  const workspaceFolder = vscode.workspace.getWorkspaceFolder(doc.uri);

  const relativePath = workspaceFolder
    ? vscode.workspace.asRelativePath(doc.uri, false)
    : doc.fileName;

  const selection = editor.selection;
  const lineRange = selection.isEmpty
    ? null
    : `L${selection.start.line + 1}-L${selection.end.line + 1}`;

  const gitRemoteHash = workspaceFolder
    ? getGitRemoteHash(workspaceFolder.uri.fsPath)
    : null;

  return {
    relativePath,
    language: doc.languageId,
    lineRange,
    gitRemoteHash,
  };
}

export function buildCaptureText(selectedText: string | null, context: WorkspaceContext): string {
  const parts: string[] = [];

  if (context.relativePath) {
    const location = context.lineRange
      ? `${context.relativePath}:${context.lineRange}`
      : context.relativePath;
    parts.push(`[${context.language}] ${location}`);
  }

  if (selectedText) {
    parts.push(selectedText);
  }

  if (context.gitRemoteHash) {
    parts.push(`workspace: ${context.gitRemoteHash}`);
  }

  return parts.join('\n\n');
}

function isLocalAbsolutePath(fsPath: string): boolean {
  if (process.platform === 'win32') {
    // Reject UNC paths (\\server\share) to prevent NTLM credential relay
    if (fsPath.startsWith('\\\\') || fsPath.startsWith('//')) return false;
    return path.isAbsolute(fsPath);
  }
  return path.isAbsolute(fsPath);
}

function getGitRemoteHash(cwd: string): string | null {
  if (!isLocalAbsolutePath(cwd)) return null;

  try {
    const remote = execSync('git remote get-url origin', {
      cwd,
      encoding: 'utf-8',
      timeout: 3000,
      stdio: ['pipe', 'pipe', 'pipe'],
    }).trim();

    if (!remote) return null;

    return createHash('sha256').update(remote).digest('hex').slice(0, 12);
  } catch {
    return null;
  }
}
