export function parseTaskdeckApiBaseUrl(value: string): URL {
  const trimmed = value.trim();
  if (!trimmed) {
    throw new Error('API URL cannot be empty.');
  }

  const parsed = new URL(trimmed);
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error('Taskdeck API URL must use HTTP or HTTPS.');
  }

  if (parsed.username || parsed.password) {
    throw new Error('Taskdeck API URL must not include embedded credentials.');
  }

  const normalizedPath = parsed.pathname.replace(/\/+$/, '');
  if (normalizedPath.length > 0) {
    throw new Error('Taskdeck API URL must not include a path.');
  }

  if (parsed.protocol === 'http:' && !isLoopbackHost(parsed.hostname)) {
    throw new Error('HTTP Taskdeck API URLs are only allowed for localhost or loopback addresses.');
  }

  return parsed;
}

function isLoopbackHost(hostname: string): boolean {
  const normalized = hostname.toLowerCase();
  const ipv4Parts = normalized.split('.');

  return normalized === 'localhost' ||
    (ipv4Parts.length === 4 &&
      ipv4Parts.every((part) => /^\d+$/.test(part) && Number(part) >= 0 && Number(part) <= 255) &&
      Number(ipv4Parts[0]) === 127) ||
    normalized === '[::1]' ||
    normalized === '::1';
}
