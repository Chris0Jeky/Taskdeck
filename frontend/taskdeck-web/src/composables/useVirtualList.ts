import { type ComputedRef, type Ref, computed, onMounted, onUpdated, ref, shallowRef } from 'vue'
import { type VirtualItem, useVirtualizer } from '@tanstack/vue-virtual'

export interface UseVirtualListOptions {
  /** Total number of items in the list. */
  count: Ref<number> | (() => number)
  /** Estimated height of each item in pixels. */
  estimateSize: number
  /** Overscan — number of items to render above/below the visible area. */
  overscan?: number
}

export interface UseVirtualListReturn {
  /** Ref to bind to the scrollable parent container element. */
  parentRef: Ref<HTMLElement | null>
  /** Ref to bind (via template ref) to rendered virtual item elements for measurement. */
  virtualItemEls: Ref<HTMLElement[]>
  /** The virtual items currently in the render window. */
  virtualRows: ComputedRef<VirtualItem[]>
  /** Total height in pixels for the inner spacer element. */
  totalSize: ComputedRef<number>
  /** Pixel offset for the translateY wrapper (start of first visible item). */
  translateY: ComputedRef<number>
  /** Scroll to a specific item index. */
  scrollToIndex: (index: number) => void
}

/**
 * Reusable composable wrapping @tanstack/vue-virtual for Taskdeck list surfaces.
 *
 * Usage:
 * 1. Bind `parentRef` to the scrollable container element.
 * 2. Bind `virtualItemEls` as a template ref on each rendered virtual row.
 * 3. Use `virtualRows` to drive the v-for loop.
 * 4. Apply `totalSize` as the height of an inner spacer div.
 * 5. Apply `translateY` as a CSS translateY on the row wrapper.
 */
export function useVirtualList(options: UseVirtualListOptions): UseVirtualListReturn {
  const { estimateSize, overscan = 5 } = options

  const parentRef = ref<HTMLElement | null>(null)
  const virtualItemEls = shallowRef<HTMLElement[]>([])

  const getCount = typeof options.count === 'function'
    ? options.count as () => number
    : () => (options.count as Ref<number>).value

  const rowVirtualizer = useVirtualizer(
    computed(() => ({
      count: getCount(),
      getScrollElement: () => parentRef.value,
      estimateSize: () => estimateSize,
      overscan,
    })),
  )

  const virtualRows = computed(() => rowVirtualizer.value.getVirtualItems())
  const totalSize = computed(() => rowVirtualizer.value.getTotalSize())
  const translateY = computed(() => virtualRows.value[0]?.start ?? 0)

  function measureAll() {
    virtualItemEls.value.forEach((el) => {
      if (el) rowVirtualizer.value.measureElement(el)
    })
  }

  onMounted(measureAll)
  // NOTE: onUpdated(measureAll) re-measures every visible element on each Vue
  // update cycle, which can be heavier than necessary when updates are unrelated
  // to item sizing. An alternative is to have each consuming component call
  // measureElement per-item via a template ref callback. We keep the blanket
  // approach here because (a) the virtual window limits the element count to
  // ~overscan*2 items, and (b) changing to per-item measurement would require
  // template changes in every consumer (InboxView, ActivityView).
  onUpdated(measureAll)

  function scrollToIndex(index: number) {
    rowVirtualizer.value.scrollToIndex(index, { align: 'auto' })
  }

  return {
    parentRef,
    virtualItemEls,
    virtualRows,
    totalSize,
    translateY,
    scrollToIndex,
  }
}
