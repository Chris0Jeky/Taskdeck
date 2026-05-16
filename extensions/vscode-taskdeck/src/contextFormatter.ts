export interface WorkspaceContext {
  relativePath: string | null;
  language: string;
  lineRange: string | null;
  gitRemoteHash: string | null;
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
