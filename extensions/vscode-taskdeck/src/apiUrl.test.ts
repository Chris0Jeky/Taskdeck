import * as assert from 'node:assert';
import { parseTaskdeckApiBaseUrl } from './apiUrl';

assert.equal(parseTaskdeckApiBaseUrl(' http://localhost:5000 ').origin, 'http://localhost:5000');
assert.equal(parseTaskdeckApiBaseUrl('https://taskdeck.example').protocol, 'https:');

assert.throws(() => parseTaskdeckApiBaseUrl(''), /cannot be empty/i);
assert.throws(() => parseTaskdeckApiBaseUrl('file:///tmp/taskdeck.sock'), /HTTP or HTTPS/i);
assert.throws(() => parseTaskdeckApiBaseUrl('taskdeck://localhost'), /HTTP or HTTPS/i);
