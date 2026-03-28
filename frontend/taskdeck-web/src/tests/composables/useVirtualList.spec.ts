import { describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { useVirtualList } from '../../composables/useVirtualList'

/**
 * Note: @tanstack/vue-virtual requires real scroll container dimensions to
 * compute virtual items. In happy-dom (headless), elements have 0 dimensions,
 * so the virtualizer returns no virtual items. These tests validate the
 * composable's reactive wiring, API surface, and totalSize computation.
 * Full render-window behavior is validated in E2E tests with real browsers.
 */

function createTestComponent(itemCount: number, estimateSize = 40) {
  return defineComponent({
    setup() {
      const count = ref(itemCount)
      const {
        parentRef,
        virtualItemEls,
        virtualRows,
        totalSize,
        translateY,
        scrollToIndex,
      } = useVirtualList({
        count,
        estimateSize,
        overscan: 3,
      })

      return {
        count,
        parentRef,
        virtualItemEls,
        virtualRows,
        totalSize,
        translateY,
        scrollToIndex,
      }
    },
    render() {
      return h(
        'div',
        {
          ref: 'parentRef',
          style: { height: '200px', width: '300px', overflow: 'auto' },
        },
        [
          h(
            'div',
            { style: { height: `${this.totalSize}px`, position: 'relative' } },
            [
              h(
                'div',
                {
                  style: {
                    position: 'absolute',
                    top: '0',
                    left: '0',
                    width: '100%',
                    transform: `translateY(${this.translateY}px)`,
                  },
                },
                this.virtualRows.map((row: { key: string | number; index: number }) =>
                  h('div', {
                    key: row.key,
                    'data-index': row.index,
                    ref: 'virtualItemEls',
                    style: { height: `${estimateSize}px` },
                  }, `Item ${row.index}`),
                ),
              ),
            ],
          ),
        ],
      )
    },
  })
}

describe('useVirtualList', () => {
  it('returns required properties from the composable', () => {
    const wrapper = mount(createTestComponent(100))
    const vm = wrapper.vm as unknown as {
      parentRef: HTMLElement | null
      virtualRows: unknown[]
      totalSize: number
      translateY: number
      scrollToIndex: (index: number) => void
    }

    expect(vm.totalSize).toBeGreaterThan(0)
    expect(typeof vm.scrollToIndex).toBe('function')
    expect(Array.isArray(vm.virtualRows)).toBe(true)
    expect(typeof vm.translateY).toBe('number')

    wrapper.unmount()
  })

  it('computes totalSize as count times estimateSize', () => {
    const wrapper = mount(createTestComponent(50, 60))
    const vm = wrapper.vm as unknown as { totalSize: number }

    expect(vm.totalSize).toBe(50 * 60)

    wrapper.unmount()
  })

  it('computes totalSize correctly for large lists', () => {
    const wrapper = mount(createTestComponent(1000, 40))
    const vm = wrapper.vm as unknown as { totalSize: number }

    expect(vm.totalSize).toBe(1000 * 40)

    wrapper.unmount()
  })

  it('handles an empty list gracefully', () => {
    const wrapper = mount(createTestComponent(0))
    const vm = wrapper.vm as unknown as {
      virtualRows: unknown[]
      totalSize: number
      translateY: number
    }

    expect(vm.virtualRows.length).toBe(0)
    expect(vm.totalSize).toBe(0)
    expect(vm.translateY).toBe(0)

    wrapper.unmount()
  })

  it('updates totalSize when item count changes reactively', async () => {
    const wrapper = mount(createTestComponent(10, 40))
    const vm = wrapper.vm as unknown as {
      count: number
      totalSize: number
    }

    expect(vm.totalSize).toBe(10 * 40)

    vm.count = 500
    await nextTick()

    expect(vm.totalSize).toBe(500 * 40)

    wrapper.unmount()
  })

  it('exposes scrollToIndex without throwing', () => {
    const wrapper = mount(createTestComponent(100))
    const vm = wrapper.vm as unknown as {
      scrollToIndex: (index: number) => void
    }

    expect(() => vm.scrollToIndex(50)).not.toThrow()
    expect(() => vm.scrollToIndex(0)).not.toThrow()
    expect(() => vm.scrollToIndex(99)).not.toThrow()

    wrapper.unmount()
  })

  it('accepts count as a ref and reacts to changes', async () => {
    const Component = defineComponent({
      setup() {
        const itemCount = ref(20)
        const { totalSize } = useVirtualList({
          count: itemCount,
          estimateSize: 30,
        })
        return { itemCount, totalSize }
      },
      render() {
        return h('div', `Total: ${this.totalSize}`)
      },
    })

    const wrapper = mount(Component)
    const vm = wrapper.vm as unknown as {
      itemCount: number
      totalSize: number
    }

    expect(vm.totalSize).toBe(20 * 30)

    vm.itemCount = 100
    await nextTick()

    expect(vm.totalSize).toBe(100 * 30)

    wrapper.unmount()
  })

  it('renders the scroll container with the parent ref', () => {
    const wrapper = mount(createTestComponent(10))
    const container = wrapper.find('div')

    expect(container.exists()).toBe(true)
    expect(container.attributes('style')).toContain('height: 200px')
    expect(container.attributes('style')).toContain('overflow: auto')

    wrapper.unmount()
  })
})
