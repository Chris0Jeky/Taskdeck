import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import NotFoundView from '../../views/NotFoundView.vue'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workspace/home', name: 'home', component: NotFoundView },
      { path: '/workspace/boards', name: 'boards', component: NotFoundView },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: NotFoundView },
    ],
  })
}

describe('NotFoundView', () => {
  it('gives users a safe recovery state without echoing the requested URL', async () => {
    const router = makeRouter()
    await router.push('/workspace/definitely-missing?secret=do-not-display#private-fragment')
    await router.isReady()

    const wrapper = mount(NotFoundView, { global: { plugins: [router] } })

    expect(wrapper.get('h1').text()).toBe('Page not found')
    expect(wrapper.text()).toContain('We couldn’t find that Taskdeck page.')
    expect(wrapper.text()).toContain('Go to Home')
    expect(wrapper.text()).toContain('Open Boards')
    expect(wrapper.text()).not.toContain('do-not-display')
    expect(wrapper.text()).not.toContain('private-fragment')
  })

  it('links to the primary authenticated recovery destinations', () => {
    const router = makeRouter()
    const wrapper = mount(NotFoundView, { global: { plugins: [router] } })
    const links = wrapper.findAll('a')

    expect(links.map((link) => link.attributes('href'))).toEqual([
      '/workspace/home',
      '/workspace/boards',
    ])
  })
})
