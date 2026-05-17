import * as path from 'node:path';

export interface WorkspaceContext {
  relativePath: string | null;
  language: string;
  lineRange: string | null;
  gitRemoteHash: string | null;
}

export interface DocumentLabelContext {
  workspaceRelativePath: string | null;
  isUntitled: boolean;
  fileName: string;
  uriScheme: string;
  uriFsPath: string;
}

export function getSafeDocumentLabel(context: DocumentLabelContext): string | null {
  const workspaceRelativePath = context.workspaceRelativePath?.trim();
  if (workspaceRelativePath) return workspaceRelativePath;

  if (context.isUntitled) {
    return context.fileName.trim() || 'Untitled';
  }

  if (context.uriScheme === 'file') {
    const localPath = context.uriFsPath.trim() || context.fileName.trim();
    const fileName = path.basename(localPath).trim();
    return fileName || null;
  }

  return context.uriScheme.trim()
    ? `${context.uriScheme.trim()} document`
    : null;
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
