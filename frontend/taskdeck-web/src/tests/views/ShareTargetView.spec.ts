import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import ShareTargetView from '../../views/ShareTargetView.vue'

const routerMock = { push: vi.fn(), replace: vi.fn() }
const routeQuery = reactive<Record<string, string | string[]>>({})
const mockOnline = ref(true)

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery }),
  useRouter: () => routerMock,
}))

vi.mock('../../composables/useOnlineStatus', () => ({
  useOnlineStatus: () => ({ isOnline: mockOnline }),
}))

const mockGetToken = vi.fn(() => 'valid-jwt-token')
const mockIsTokenExpired = vi.fn(() => false)

vi.mock('../../utils/tokenStorage', () => ({
  getToken: () => mockGetToken(),
}))

vi.mock('../../utils/jwt', () => ({
  isTokenExpired: () => mockIsTokenExpired(),
}))

const mockCreateItem = vi.fn(async () => ({
  id: 'capture-1',
  boardId: null,
  text: 'test',
  status: 'New',
  source: 'ShareTarget',
}))

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => ({ createItem: mockCreateItem }),
}))

const mockEnqueue = vi.fn(async () => 'queued-id-1')

vi.mock('../../utils/captureQueue', () => ({
  enqueueCapture: (...args: unknown[]) => mockEnqueue(...args),
}))

function setQuery(title = '', text = '', url = '') {
  Object.keys(routeQuery).forEach((k) => delete routeQuery[k])
  if (title) routeQuery.title = title
  if (text) routeQuery.text = text
  if (url) routeQuery.url = url
}

describe('ShareTargetView', () => {
  beforeEach(() => {
    routerMock.push.mockClear()
    routerMock.replace.mockClear()
    mockCreateItem.mockClear()
    mockEnqueue.mockClear()
    mockGetToken.mockReturnValue('valid-jwt-token')
    mockIsTokenExpired.mockReturnValue(false)
    mockOnline.value = true
    setQuery()
    Reflect.deleteProperty(globalThis, 'caches')
  })

  it('sends shared content directly to capture API when online', async () => {
    setQuery('Test Title', 'Some text', 'https://example.com')
    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Test Title\n\nSome text\n\nhttps://example.com',
      source: 'ShareTarget',
      titleHint: 'Test Title',
      externalRef: 'https://example.com',
    })
    expect(mockEnqueue).not.toHaveBeenCalled()
    expect(routerMock.replace).toHaveBeenCalledWith({ name: 'capture-share-target', query: {} })
  })

  it('loads POST share-target payload from service worker cache without putting content in the URL', async () => {
    const cache = {
      match: vi.fn(async () => new Response(JSON.stringify({
        title: 'Posted Title',
        text: 'Posted text',
        url: 'https://example.com/private',
      }))),
      delete: vi.fn(async () => true),
    }
    Object.defineProperty(globalThis, 'caches', {
      configurable: true,
      value: {
        open: vi.fn(async () => cache),
      },
    })

    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Posted Title\n\nPosted text\n\nhttps://example.com/private',
      source: 'ShareTarget',
      titleHint: 'Posted Title',
      externalRef: 'https://example.com/private',
    })
    expect(cache.match).toHaveBeenCalledWith('/capture/share-data')
    expect(cache.delete).toHaveBeenCalledWith('/capture/share-data')
    expect(routerMock.replace).not.toHaveBeenCalled()
  })

  it('queues capture in IndexedDB when offline', async () => {
    mockOnline.value = false
    setQuery('Offline Title', 'Offline text', '')
    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).not.toHaveBeenCalled()
    expect(mockEnqueue).toHaveBeenCalledWith({
      boardId: null,
      text: 'Offline Title\n\nOffline text',
      source: 'ShareTarget',
      titleHint: 'Offline Title',
      externalRef: null,
    })
  })

  it('falls back to queue when API call fails', async () => {
    mockCreateItem.mockRejectedValueOnce(new Error('Network Error'))
    setQuery('Retry Title', '', 'https://example.com')
    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).toHaveBeenCalled()
    expect(mockEnqueue).toHaveBeenCalled()
  })

  it('shows error state when no content is shared', async () => {
    setQuery('', '', '')
    const wrapper = mount(ShareTargetView)
    await flushPromises()

    expect(wrapper.text()).toContain('Nothing to capture')
    expect(mockCreateItem).not.toHaveBeenCalled()
    expect(mockEnqueue).not.toHaveBeenCalled()
  })

  it('navigates to inbox when Open Inbox button is clicked', async () => {
    setQuery('Title', 'Text', '')
    const wrapper = mount(ShareTargetView)
    await flushPromises()

    await wrapper.find('.share-target-view__btn--primary').trigger('click')
    expect(routerMock.push).toHaveBeenCalledWith({ name: 'workspace-inbox' })
  })

  it('deduplicates title from text when they are identical', async () => {
    setQuery('Same content', 'Same content', '')
    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).toHaveBeenCalledWith(
      expect.objectContaining({ text: 'Same content' }),
    )
  })

  it('handles URL-only share', async () => {
    setQuery('', '', 'https://example.com/article')
    mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).toHaveBeenCalledWith(
      expect.objectContaining({
        text: 'https://example.com/article',
        externalRef: 'https://example.com/article',
        titleHint: null,
      }),
    )
  })

  it('shows login-required state when no valid session exists', async () => {
    mockGetToken.mockReturnValue(null)
    setQuery('Title', 'Text', '')
    const wrapper = mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).not.toHaveBeenCalled()
    expect(mockEnqueue).toHaveBeenCalled()
    expect(wrapper.text()).toContain('Login required')
  })

  it('shows login-required when token is expired', async () => {
    mockGetToken.mockReturnValue('expired-token')
    mockIsTokenExpired.mockReturnValue(true)
    setQuery('Title', '', 'https://example.com')
    const wrapper = mount(ShareTargetView)
    await flushPromises()

    expect(mockCreateItem).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Login required')
  })

  it('navigates to login when Log In button is clicked in login-required state', async () => {
    mockGetToken.mockReturnValue(null)
    setQuery('Title', 'Text', '')
    const wrapper = mount(ShareTargetView)
    await flushPromises()

    await wrapper.find('.share-target-view__btn--primary').trigger('click')
    expect(routerMock.push).toHaveBeenCalledWith({ name: 'login' })
  })
})
