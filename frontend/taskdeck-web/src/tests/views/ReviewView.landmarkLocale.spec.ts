import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { DEFAULT_LOCALE, ensureLocaleMessages, i18n } from '../../i18n'
import ReviewView from '../../views/ReviewView.vue'

const skin = vi.hoisted(() => ({ isOn: true }))

vi.mock('../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => skin,
}))
vi.mock('../../views/paper/PaperReviewView.vue', () => ({
  default: {
    name: 'PaperReviewViewStub',
    inheritAttrs: true,
    template: '<main data-testid="review-landmark" />',
  },
}))
vi.mock('../../views/LegacyReviewView.vue', () => ({
  default: {
    name: 'LegacyReviewViewStub',
    inheritAttrs: true,
    template: '<main data-testid="review-landmark" />',
  },
}))

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', component: ReviewView }],
  })
  await router.push('/workspace/review')
  await router.isReady()
  return mount(ReviewView, { global: { plugins: [router] } })
}

describe('Review unavailable-return landmark locale (GH-2599)', () => {
  beforeAll(async () => {
    await Promise.all([ensureLocaleMessages('it'), ensureLocaleMessages('es')])
  })

  beforeEach(() => {
    skin.isOn = true
    i18n.global.locale.value = DEFAULT_LOCALE
  })

  afterEach(() => {
    i18n.global.locale.value = DEFAULT_LOCALE
  })

  it.each([
    ['Paper', true, 'es'],
    ['Legacy', false, 'it'],
  ] as const)('keeps the %s fallback landmark in the active language', async (_label, isOn, locale) => {
    skin.isOn = isOn
    const wrapper = await mountView()
    try {
      const landmark = wrapper.get('[data-testid="review-landmark"]')
      const english = i18n.global.t('review.surfaceLabel')
      expect(english).not.toBe(i18n.global.t('review.empty.unavailable.return'))
      expect(landmark.attributes('aria-label')).toBe(english)

      i18n.global.locale.value = locale
      await nextTick()

      const localized = i18n.global.t('review.surfaceLabel')
      expect(localized).not.toBe(english)
      expect(localized).not.toBe(i18n.global.t('review.empty.unavailable.return'))
      expect(landmark.attributes('aria-label')).toBe(localized)
      expect(landmark.attributes('role')).toBe('region')
      expect(landmark.attributes('tabindex')).toBe('-1')
    } finally {
      wrapper.unmount()
    }
  })
})
