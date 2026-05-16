import * as vscode from 'vscode';
import { TaskdeckClient } from './client';
import { buildCaptureText, getWorkspaceContext } from './context';

let statusBarItem: vscode.StatusBarItem | undefined;
let client: TaskdeckClient;
let disposed = false;

export function activate(extensionContext: vscode.ExtensionContext): void {
  disposed = false;
  client = new TaskdeckClient(extensionContext);

  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 50);
  statusBarItem.text = '$(cloud-upload) Taskdeck';
  statusBarItem.tooltip = 'Taskdeck Capture';
  statusBarItem.command = 'taskdeck.captureSelection';
  statusBarItem.show();
  extensionContext.subscriptions.push(statusBarItem);

  extensionContext.subscriptions.push(
    vscode.commands.registerCommand('taskdeck.captureSelection', () => captureSelection()),
    vscode.commands.registerCommand('taskdeck.captureFile', () => captureFileContext()),
    vscode.commands.registerCommand('taskdeck.setApiUrl', () => setApiUrl()),
    vscode.commands.registerCommand('taskdeck.setToken', () => setToken(extensionContext)),
  );
}

export function deactivate(): void {
  disposed = true;
}

function setStatusText(text: string): void {
  if (!disposed && statusBarItem) {
    statusBarItem.text = text;
  }
}

async function captureSelection(): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor) {
    vscode.window.showWarningMessage('No active editor');
    return;
  }

  const selection = editor.selection;
  if (selection.isEmpty) {
    vscode.window.showWarningMessage('No text selected. Select text first, then capture.');
    return;
  }

  const selectedText = editor.document.getText(selection);
  const context = await getWorkspaceContext(editor);
  const captureText = buildCaptureText(selectedText, context);

  await sendCapture(captureText, context.relativePath, context.gitRemoteHash);
}

async function captureFileContext(): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor) {
    vscode.window.showWarningMessage('No active editor');
    return;
  }

  const context = await getWorkspaceContext(editor);
  const captureText = buildCaptureText(null, context);

  if (!captureText.trim()) {
    vscode.window.showWarningMessage('No capturable context found for this file.');
    return;
  }

  await sendCapture(captureText, context.relativePath, context.gitRemoteHash);
}

async function sendCapture(text: string, titleHint: string | null, externalRef: string | null): Promise<void> {
  setStatusText('$(sync~spin) Sending...');

  try {
    await client.createCapture({
      boardId: null,
      text,
      source: 'VsCodeExtension',
      titleHint,
      externalRef,
    });
    setStatusText('$(check) Sent');
    vscode.window.showInformationMessage('Captured to Taskdeck inbox');
  } catch (err) {
    setStatusText('$(error) Failed');
    const message = err instanceof Error ? err.message : 'Unknown error';
    vscode.window.showErrorMessage(`Taskdeck capture failed: ${message}`);
  } finally {
    setTimeout(() => {
      setStatusText('$(cloud-upload) Taskdeck');
    }, 3000);
  }
}

async function setApiUrl(): Promise<void> {
  const config = vscode.workspace.getConfiguration('taskdeck');
  const current = config.get<string>('apiUrl', 'http://localhost:5000');

  const value = await vscode.window.showInputBox({
    prompt: 'Taskdeck API URL',
    value: current,
    placeHolder: 'http://localhost:5000',
  });

  if (value === undefined) return;

  if (!value.trim()) {
    vscode.window.showWarningMessage('API URL cannot be empty.');
    return;
  }

  try {
    const parsed = new URL(value);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      throw new Error('Unsupported protocol');
    }
  } catch {
    vscode.window.showErrorMessage(`Invalid URL: "${value}". Please enter a valid HTTP(S) URL.`);
    return;
  }

  await config.update('apiUrl', value, vscode.ConfigurationTarget.Global);
  vscode.window.showInformationMessage(`API URL updated`);
}

async function setToken(extensionContext: vscode.ExtensionContext): Promise<void> {
  const value = await vscode.window.showInputBox({
    prompt: 'Paste your Taskdeck JWT token',
    password: true,
    placeHolder: 'eyJ...',
  });

  if (value === undefined) return;

  if (!value.trim()) {
    vscode.window.showWarningMessage('Token cannot be empty. Run the command again and paste your JWT.');
    return;
  }

  await extensionContext.secrets.store('taskdeck.token', value);
  vscode.window.showInformationMessage('Token saved securely');
}
