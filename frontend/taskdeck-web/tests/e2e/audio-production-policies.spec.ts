import { readFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'

const apiPolicy = JSON.parse(readFileSync(new URL('../../../../backend/src/Taskdeck.Api/appsettings.json', import.meta.url), 'utf8')).SecurityHeaders.ContentSecurityPolicy as string
const proxies = ['deploy/nginx/reverse-proxy.conf', 'deploy/terraform/aws/modules/single_node/user_data.sh.tftpl'].map(path => {
  const source = readFileSync(new URL(`../../../../${path}`, import.meta.url), 'utf8')
  return { name: path, csp: /add_header Content-Security-Policy "([^"]+)"/.exec(source)![1]!, permissions: /add_header Permissions-Policy "([^"]+)"/.exec(source)![1]! }
})
for (const policy of [{ name: 'single-container API', csp: apiPolicy, permissions: '' }, ...proxies]) {
  test(`${policy.name} lets the browser read local audio and request its own microphone`, async ({ page }) => {
    await page.route('**/audio-policy-proof', route => route.fulfill({ status: 200, contentType: 'text/html', headers: {
      'Content-Security-Policy': policy.csp, ...(policy.permissions ? { 'Permissions-Policy': policy.permissions } : {}),
    }, body: '<!doctype html><html lang="en"><title>Audio policy proof</title><body><h1>Local audio</h1></body></html>' }))
    await page.goto('/audio-policy-proof')
    const result = await page.evaluate(async () => {
      const wav = new ArrayBuffer(1644); const view = new DataView(wav)
      const text = (offset: number, value: string) => [...value].forEach((char, index) => view.setUint8(offset + index, char.charCodeAt(0)))
      text(0, 'RIFF'); view.setUint32(4, 1636, true); text(8, 'WAVE'); text(12, 'fmt ')
      view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true)
      view.setUint32(24, 8000, true); view.setUint32(28, 16000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true)
      text(36, 'data'); view.setUint32(40, 1600, true)
      const url = URL.createObjectURL(new Blob([wav], { type: 'audio/wav' }))
      const audio = document.createElement('audio'); audio.preload = 'metadata'; document.body.append(audio)
      try {
        const duration = await new Promise<number>((resolve, reject) => {
          const timeout = setTimeout(() => reject(new Error('Audio metadata did not load')), 8000)
          audio.onloadedmetadata = () => { clearTimeout(timeout); resolve(audio.duration) }
          audio.onerror = () => { clearTimeout(timeout); reject(new Error(`Audio blocked: ${audio.error?.message}`)) }
          audio.src = url
        })
        const permissions = (document as Document & { featurePolicy?: { allowsFeature(name: string): boolean } }).featurePolicy
        return { duration, microphone: permissions?.allowsFeature('microphone'), camera: permissions?.allowsFeature('camera'), geolocation: permissions?.allowsFeature('geolocation') }
      } finally { audio.remove(); URL.revokeObjectURL(url) }
    })
    expect(result.duration).toBeCloseTo(.1)
    expect(result.microphone).toBe(true)
    if (policy.permissions) { expect(result.camera).toBe(false); expect(result.geolocation).toBe(false) }
  })
}
