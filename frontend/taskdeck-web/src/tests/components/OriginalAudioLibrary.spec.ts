import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import OriginalAudioLibrary from '../../components/workspace/OriginalAudioLibrary.vue'
import type { AudioLibraryDetail, AudioLibraryEntry } from '../../api/thinkingAudioApi'

enableAutoUnmount(afterEach)
const api = vi.hoisted(() => ({ library: vi.fn(), libraryDetail: vi.fn(), libraryOriginal: vi.fn() }))
const session = reactive({ userId: 'owner', token: 'token' })
vi.mock('../../api/thinkingAudioApi', () => ({ thinkingAudioApi: api }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
const item: AudioLibraryEntry = { id: 'r1', fileName: 'voice.wav', byteSize: 1024, createdAt: '', questionExcerpt: 'Original question', hasWrittenVersion: false, hasConfirmedAnswer: false, boardRemoved: false }
const receipt: AudioLibraryDetail = { currentBoardId: 'b1', currentCardId: 'c1', recording: { id: 'r1', revision: 1, captureId: 'capture', sourceAssetId: 'asset', questionHash: 'hash', fileName: 'voice.wav', byteSize: 1024, mediaType: 'audio/wav', contentHash: 'digest', originalEvidence: 'Exact original question', representationId: null, confirmedMemoryId: null, writtenVersions: [] } }
const render = () => mount(OriginalAudioLibrary, { global: { stubs: { RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' } } } })
const button = (wrapper: ReturnType<typeof render>, text: string) => wrapper.findAll('button').find(x => x.text() === text)!

describe('private original library', () => {
  it('distinguishes prior confirmation from a surviving private memory after board removal', async () => {
    api.library.mockResolvedValue({ items: [{ ...item, boardRemoved: true, hasWrittenVersion: true, hasConfirmedAnswer: true }], nextOffset: null })
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Previously confirmed; written version retained')
    expect(wrapper.text()).not.toContain('Written version, unconfirmed')
    expect(wrapper.text()).not.toContain('Confirmed answer kept separately')
  })
  beforeEach(() => {
    vi.clearAllMocks(); session.userId = 'owner'; session.token = 'token'
    api.library.mockResolvedValue({ items: [item], nextOffset: null })
    api.libraryDetail.mockResolvedValue(receipt)
    api.libraryOriginal.mockResolvedValue(new Blob(['audio']))
    vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:original'), revokeObjectURL: vi.fn() })
  })
  afterEach(() => vi.unstubAllGlobals())
  it('loads only on explicit request, inspects original evidence and offers a current-question link', async () => {
    const wrapper = render(); expect(api.library).not.toHaveBeenCalled()
    await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    expect(api.library).toHaveBeenCalledWith(0)
    expect(wrapper.text()).toContain('Untranscribed original')
    await button(wrapper, 'Inspect recording').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Exact original question')
    expect(wrapper.get('a').attributes('href')).toBe('/workspace/boards/b1/cards/c1/thinking')
    await button(wrapper, 'Load selected original for playback or download').trigger('click'); await flushPromises()
    expect(api.libraryOriginal).toHaveBeenCalledWith('r1')
    expect(wrapper.get('audio').attributes('src')).toBe('blob:original')
    wrapper.unmount(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:original')
  })
  it('keeps deleted-board originals read-only with visible written alternatives', async () => {
    api.library.mockResolvedValue({ items: [{ ...item, boardRemoved: true, hasWrittenVersion: true }], nextOffset: null })
    api.libraryDetail.mockResolvedValue({ ...receipt, currentBoardId: null, currentCardId: null, recording: { ...receipt.recording, writtenVersions: [{ id: 'v1', text: 'Earlier words', quality: 'Superseded', supersededById: 'v2' }] } })
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    await button(wrapper, 'Inspect recording').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Board removed'); expect(wrapper.text()).toContain('read-only')
    expect(wrapper.text()).toContain('Earlier words'); expect(wrapper.find('a').exists()).toBe(false)
    expect(wrapper.find('textarea').exists()).toBe(false)
  })
  it('advances an empty filtered page and can return without appending stale rows', async () => {
    api.library.mockResolvedValueOnce({ items: [], nextOffset: 20 }).mockResolvedValueOnce({ items: [item], nextOffset: null }).mockResolvedValueOnce({ items: [], nextOffset: 20 })
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    await button(wrapper, 'Next recordings').trigger('click'); await flushPromises()
    expect(api.library).toHaveBeenLastCalledWith(20)
    await button(wrapper, 'Previous recordings').trigger('click'); await flushPromises()
    expect(api.library).toHaveBeenLastCalledWith(0); expect(wrapper.findAll('li')).toHaveLength(0)
  })
  it('shows permission errors and supports explicit retry', async () => {
    api.library.mockRejectedValueOnce({ response: { status: 403 } })
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('no longer available')
    await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    expect(wrapper.find('[role=alert]').exists()).toBe(false)
  })
  it.each(['account', 'logout', 'unmount'])('discards a late original download after %s', async change => {
    let finish!: (value: Blob) => void
    api.libraryOriginal.mockReturnValueOnce(new Promise<Blob>(resolve => { finish = resolve }))
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    await button(wrapper, 'Inspect recording').trigger('click'); await flushPromises()
    await button(wrapper, 'Load selected original for playback or download').trigger('click')
    if (change === 'account') session.userId = 'other'
    else if (change === 'logout') session.token = ''
    else wrapper.unmount()
    finish(new Blob(['audio'])); await flushPromises()
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    if (change !== 'unmount') expect(wrapper.find('article').exists()).toBe(false)
  })
  it('preserves a pending owner request across same-user token refresh', async () => {
    let finish!: (value: { items: AudioLibraryEntry[]; nextOffset: null }) => void
    api.library.mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click')
    session.token = 'refreshed'; finish({ items: [item], nextOffset: null }); await flushPromises()
    expect(wrapper.text()).toContain('voice.wav')
  })
  it('does not display a superseded detail request after choosing another original', async () => {
    let finish!: (value: AudioLibraryDetail) => void
    api.libraryDetail.mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
    const wrapper = render(); await button(wrapper, 'Browse original recordings').trigger('click'); await flushPromises()
    await button(wrapper, 'Inspect recording').trigger('click')
    await button(wrapper, 'Reload recording library').trigger('click'); await flushPromises()
    finish(receipt); await flushPromises(); expect(wrapper.find('article').exists()).toBe(false)
  })
})
