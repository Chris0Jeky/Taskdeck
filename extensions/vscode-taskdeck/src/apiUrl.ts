export function parseTaskdeckApiBaseUrl(value: string): URL {
  const trimmed = value.trim();
  if (!trimmed) {
    throw new Error('API URL cannot be empty.');
  }

  const parsed = new URL(trimmed);
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('Taskdeck API URL must use HTTP or HTTPS.');
  }

  return parsed;
}
