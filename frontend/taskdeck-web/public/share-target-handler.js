const TASKDECK_SHARE_CACHE = 'taskdeck-share-target'
const TASKDECK_SHARE_REQUEST_PREFIX = '/capture/share-data/'
const TASKDECK_CAPTURE_SYNC_MESSAGE = 'taskdeck:capture-sync'

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url)
  if (
    event.request.method !== 'POST' ||
    url.origin !== self.location.origin ||
    url.pathname !== '/capture/share'
  ) {
    return
  }

  event.respondWith((async () => {
    const form = await event.request.formData()
    const payload = {
      title: String(form.get('title') ?? ''),
      text: String(form.get('text') ?? ''),
      url: String(form.get('url') ?? ''),
    }

    const cache = await caches.open(TASKDECK_SHARE_CACHE)
    const shareId = createShareId()
    await cache.put(
      `${TASKDECK_SHARE_REQUEST_PREFIX}${shareId}`,
      new Response(JSON.stringify(payload), {
        headers: {
          'Content-Type': 'application/json',
          'Cache-Control': 'no-store',
        },
      }),
    )

    return Response.redirect(`/capture/share?fromShareTarget=1&shareId=${encodeURIComponent(shareId)}`, 303)
  })())
})

self.addEventListener('message', (event) => {
  if (event.data?.type !== TASKDECK_CAPTURE_SYNC_MESSAGE) {
    return
  }

  event.waitUntil(notifyWindowClientsToReplayCaptureQueue())
})

async function notifyWindowClientsToReplayCaptureQueue() {
  const clients = await self.clients.matchAll({
    type: 'window',
    includeUncontrolled: true,
  })

  for (const client of clients) {
    client.postMessage({ type: TASKDECK_CAPTURE_SYNC_MESSAGE })
  }
}

function createShareId() {
  if (self.crypto?.randomUUID) {
    return self.crypto.randomUUID()
  }

  const random = Math.random().toString(36).slice(2)
  return `${Date.now().toString(36)}-${random}`
}
