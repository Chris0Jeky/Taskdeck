import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useToastStore } from '../../store/toastStore'

describe('toastStore', () => {
  let store: ReturnType<typeof useToastStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useToastStore()
    vi.clearAllMocks()
    vi.useRealTimers()
  })

  describe('show', () => {
    it('should add a toast with default type and duration', () => {
      store.show('Hello')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].message).toBe('Hello')
      expect(store.toasts[0].type).toBe('info')
      expect(store.toasts[0].duration).toBe(3000)
    })

    it('should add a toast with custom type and duration', () => {
      store.show('Error occurred', 'error', 5000)

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('error')
      expect(store.toasts[0].duration).toBe(5000)
    })

    it('should return the toast ID', () => {
      const id = store.show('Toast message')

      expect(id).toBeTruthy()
      expect(typeof id).toBe('string')
    })
  })

  describe('convenience methods', () => {
    it('should add a success toast', () => {
      store.success('Done!')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('success')
      expect(store.toasts[0].duration).toBe(3000)
    })

    it('should add an error toast with default 5s duration', () => {
      store.error('Failed!')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('error')
      expect(store.toasts[0].duration).toBe(5000)
    })

    it('should add an info toast', () => {
      store.info('FYI')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('info')
    })

    it('should add a warning toast with default 4s duration', () => {
      store.warning('Watch out!')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('warning')
      expect(store.toasts[0].duration).toBe(4000)
    })
  })

  describe('remove', () => {
    it('should remove a toast by ID', () => {
      const id = store.show('Toast to remove')

      expect(store.toasts).toHaveLength(1)

      store.remove(id)

      expect(store.toasts).toHaveLength(0)
    })

    it('should not throw when removing a non-existent ID', () => {
      store.show('Toast')

      store.remove('non-existent-id')

      expect(store.toasts).toHaveLength(1)
    })
  })

  describe('clear', () => {
    it('should remove all toasts', () => {
      store.show('Toast 1')
      store.show('Toast 2')
      store.show('Toast 3')

      expect(store.toasts).toHaveLength(3)

      store.clear()

      expect(store.toasts).toHaveLength(0)
    })
  })

  describe('auto-removal', () => {
    it('should auto-remove toast after its duration', () => {
      vi.useFakeTimers()

      store.show('Temporary toast', 'info', 3000)

      expect(store.toasts).toHaveLength(1)

      vi.advanceTimersByTime(3000)

      expect(store.toasts).toHaveLength(0)
    })

    it('should not auto-remove toast when duration is 0', () => {
      vi.useFakeTimers()

      store.show('Persistent toast', 'info', 0)

      expect(store.toasts).toHaveLength(1)

      vi.advanceTimersByTime(10000)

      expect(store.toasts).toHaveLength(1)
    })
  })

  describe('multiple toasts', () => {
    it('should support multiple toasts simultaneously', () => {
      store.success('Success')
      store.error('Error')
      store.warning('Warning')

      expect(store.toasts).toHaveLength(3)
      expect(store.toasts[0].type).toBe('success')
      expect(store.toasts[1].type).toBe('error')
      expect(store.toasts[2].type).toBe('warning')
    })

    it('should auto-remove toasts independently based on their duration', () => {
      vi.useFakeTimers()

      store.success('Quick', 1000)
      store.error('Slow', 5000)

      expect(store.toasts).toHaveLength(2)

      vi.advanceTimersByTime(1000)

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('error')

      vi.advanceTimersByTime(4000)

      expect(store.toasts).toHaveLength(0)
    })
  })
})
