import * as assert from 'node:assert';
import { StatusResetGuard } from './statusReset';

const guard = new StatusResetGuard();

const first = guard.nextGeneration();
const second = guard.nextGeneration();

assert.equal(guard.isCurrent(first), false, 'older status reset generations must not remain current');
assert.equal(guard.isCurrent(second), true, 'latest status reset generation remains current');
