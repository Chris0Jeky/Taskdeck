import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import assert from 'node:assert/strict'

for (const path of ['deploy/nginx/reverse-proxy.conf', 'deploy/terraform/aws/modules/single_node/user_data.sh.tftpl']) {
  test(`${path} permits local audio without opening other capture permissions`, () => {
    const source = readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8')
    const policies = [...source.matchAll(/add_header Permissions-Policy "([^"]+)"/g)]
    assert.equal(policies.length, 1)
    assert.equal(policies[0][1], 'geolocation=(), microphone=(self), camera=()')
    const csp = [...source.matchAll(/add_header Content-Security-Policy "([^"]+)"/g)]
    assert.equal(csp.length, 1)
    assert.deepEqual(csp[0][1].split(';').map(value => value.trim()).filter(value => value.startsWith('media-src ')), ["media-src 'self' blob:"])
  })
}
