import * as vscode from 'vscode';
import { createHash } from 'node:crypto';
import * as path from 'node:path';
import { buildCaptureText, getSafeDocumentLabel, type WorkspaceContext } from './contextFormatter';

export type { WorkspaceContext } from './contextFormatter';

interface GitRemote {
  name: string;
  fetchUrl?: string;
  pushUrl?: string;
}

interface GitRepository {
  rootUri: vscode.Uri;
  state: {
    remotes: GitRemote[];
  };
}

interface GitApi {
  repositories: GitRepository[];
}

interface GitExtension {
  getAPI(version: number): GitApi;
}

export async function getWorkspaceContext(editor: vscode.TextEditor): Promise<WorkspaceContext> {
  const doc = editor.document;
  const workspaceFolder = vscode.workspace.getWorkspaceFolder(doc.uri);

  const relativePath = getSafeDocumentLabel({
    workspaceRelativePath: workspaceFolder
      ? vscode.workspace.asRelativePath(doc.uri, false)
      : null,
    isUntitled: doc.isUntitled,
    fileName: doc.fileName,
    uriScheme: doc.uri.scheme,
    uriFsPath: doc.uri.fsPath,
  });

  const selection = editor.selection;
  const lineRange = selection.isEmpty
    ? null
    : `L${selection.start.line + 1}-L${selection.end.line + 1}`;

  const gitRemoteHash = workspaceFolder
    ? await getGitRemoteHash(workspaceFolder)
    : null;

  return {
    relativePath,
    language: doc.languageId,
    lineRange,
    gitRemoteHash,
  };
}

export { buildCaptureText };

function isLocalAbsolutePath(fsPath: string): boolean {
  if (process.platform === 'win32') {
    // Reject UNC paths (\\server\share) to prevent NTLM credential relay
    if (fsPath.startsWith('\\\\') || fsPath.startsWith('//')) return false;
    return path.isAbsolute(fsPath);
  }
  return path.isAbsolute(fsPath);
}

function normalizeFsPath(fsPath: string): string {
  return process.platform === 'win32'
    ? fsPath.toLowerCase()
    : fsPath;
}

async function getGitRemoteHash(workspaceFolder: vscode.WorkspaceFolder): Promise<string | null> {
  if (!isLocalAbsolutePath(workspaceFolder.uri.fsPath)) return null;

  const gitExtension = vscode.extensions.getExtension<GitExtension>('vscode.git');
  if (!gitExtension) return null;

  try {
    const extensionApi = gitExtension.isActive
      ? gitExtension.exports
      : await gitExtension.activate();
    const api = extensionApi.getAPI(1);
    const workspacePath = normalizeFsPath(workspaceFolder.uri.fsPath);
    const repository = api.repositories.find((repo) => normalizeFsPath(repo.rootUri.fsPath) === workspacePath);
    const remote = repository?.state.remotes.find((candidate) => candidate.name === 'origin')
      ?? repository?.state.remotes[0];
    const remoteUrl = (remote?.fetchUrl ?? remote?.pushUrl ?? '').trim();

    if (!remoteUrl) return null;

    return createHash('sha256').update(remoteUrl).digest('hex').slice(0, 12);
  } catch {
    return null;
  }
}
