import * as vscode from 'vscode';
import * as https from 'node:https';
import * as http from 'node:http';

export interface CreateCaptureDto {
  boardId: string | null;
  text: string;
  source: string;
  titleHint: string | null;
  externalRef: string | null;
}

interface CaptureResponse {
  id: string;
  status: string;
}

export class TaskdeckClient {
  constructor(private readonly context: vscode.ExtensionContext) {}

  async createCapture(dto: CreateCaptureDto): Promise<CaptureResponse> {
    const apiUrl = this.getApiUrl();
    const token = await this.getToken();

    if (!token) {
      throw new Error('No auth token configured. Run "Taskdeck: Set Authentication Token" first.');
    }

    const url = new URL('/api/capture/items', apiUrl);
    const body = JSON.stringify(dto);

    return new Promise<CaptureResponse>((resolve, reject) => {
      const transport = url.protocol === 'https:' ? https : http;
      const req = transport.request(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`,
          'Content-Length': Buffer.byteLength(body),
        },
      }, (res) => {
        let data = '';
        res.on('data', (chunk: Buffer) => { data += chunk.toString(); });
        res.on('end', () => {
          if (res.statusCode === 401) {
            reject(new Error('Authentication failed. Update your token with "Taskdeck: Set Authentication Token".'));
            return;
          }
          if (!res.statusCode || res.statusCode >= 400) {
            reject(new Error(`API returned ${res.statusCode}: ${data}`));
            return;
          }
          try {
            resolve(JSON.parse(data) as CaptureResponse);
          } catch {
            reject(new Error(`Invalid response: ${data}`));
          }
        });
      });

      req.on('error', (err) => reject(new Error(`Network error: ${err.message}`)));
      req.write(body);
      req.end();
    });
  }

  private getApiUrl(): string {
    return vscode.workspace.getConfiguration('taskdeck').get<string>('apiUrl', 'http://localhost:5000');
  }

  private async getToken(): Promise<string | undefined> {
    return this.context.secrets.get('taskdeck.token');
  }
}
