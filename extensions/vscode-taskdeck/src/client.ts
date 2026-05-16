import * as vscode from 'vscode';
import * as https from 'node:https';
import * as http from 'node:http';

const REQUEST_TIMEOUT_MS = 10_000;
const MAX_RESPONSE_BYTES = 65_536;

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

    let url: URL;
    try {
      url = new URL('/api/capture/items', apiUrl);
    } catch {
      throw new Error(`Invalid API URL: "${apiUrl}". Run "Taskdeck: Set API URL" to fix.`);
    }

    const body = JSON.stringify(dto);

    return new Promise<CaptureResponse>((resolve, reject) => {
      const transport = url.protocol === 'https:' ? https : http;
      let settled = false;

      const fail = (error: Error) => {
        if (settled) return;
        settled = true;
        reject(error);
      };

      const succeed = (response: CaptureResponse) => {
        if (settled) return;
        settled = true;
        resolve(response);
      };

      const req = transport.request(url, {
        method: 'POST',
        timeout: REQUEST_TIMEOUT_MS,
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`,
          'Content-Length': Buffer.byteLength(body),
        },
      }, (res) => {
        let data = '';
        let bytesReceived = 0;

        res.on('error', (err) => fail(new Error(`Response stream error: ${err.message}`)));
        res.on('aborted', () => fail(new Error('Response aborted before completion')));
        res.on('close', () => {
          if (!res.complete) {
            fail(new Error('Response closed before completion'));
          }
        });
        res.on('data', (chunk: Buffer) => {
          if (settled) return;
          bytesReceived += chunk.length;
          if (bytesReceived > MAX_RESPONSE_BYTES) {
            fail(new Error('Response too large'));
            res.destroy();
            return;
          }
          data += chunk.toString();
        });

        res.on('end', () => {
          if (settled) return;
          if (res.statusCode === 401) {
            fail(new Error('Authentication failed. Update your token with "Taskdeck: Set Authentication Token".'));
            return;
          }
          if (!res.statusCode || res.statusCode >= 400) {
            fail(new Error(`API returned ${res.statusCode}: ${data}`));
            return;
          }
          try {
            succeed(JSON.parse(data) as CaptureResponse);
          } catch {
            fail(new Error(`Invalid response: ${data}`));
          }
        });
      });

      req.on('timeout', () => {
        req.destroy(new Error('Request timed out'));
      });
      req.on('error', (err) => fail(new Error(`Network error: ${err.message}`)));
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
