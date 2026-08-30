import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { copyToastReceipt, toastReceiptText, useToastStore } from '../../store/toastStore'

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

    it('should keep an error toast as a durable receipt by default', () => {
      store.error('Failed!')

      expect(store.toasts).toHaveLength(1)
      expect(store.toasts[0].type).toBe('error')
      expect(store.toasts[0].duration).toBe(0)
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

    it('should pause and resume auto-removal timers', () => {
      vi.useFakeTimers()

      const id = store.show('Pausable toast', 'info', 3000)
      vi.advanceTimersByTime(1000)

      store.pause(id)
      vi.advanceTimersByTime(5000)

      expect(store.toasts).toHaveLength(1)

      store.resume(id)
      vi.advanceTimersByTime(1999)
      expect(store.toasts).toHaveLength(1)

      vi.advanceTimersByTime(1)
      expect(store.toasts).toHaveLength(0)
    })
  })

  describe('error receipts', () => {
    it('copies the message and optional details as one receipt', () => {
      const toast = { message: 'Request failed', details: 'status: 503\nrequest id: abc' }

      expect(toastReceiptText(toast)).toBe('Request failed\n\nstatus: 503\nrequest id: abc')
      expect(toastReceiptText({ message: 'Request failed' })).toBe('Request failed')
    })

    it('falls back to a textarea when the async clipboard is unavailable', async () => {
      const execCommand = vi.fn().mockReturnValue(true)
      Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
      const toast = { message: 'Request failed', details: 'status: 503' }

      expect(await copyToastReceipt(toast)).toBe(true)
      expect(execCommand).toHaveBeenCalledWith('copy')
      expect(document.querySelector('textarea')).toBeNull()
      Reflect.deleteProperty(document, 'execCommand')
      Reflect.deleteProperty(navigator, 'clipboard')
    })

    it('prefers the async clipboard when it is available', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined)
      const createElement = vi.spyOn(document, 'createElement')
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: { writeText },
      })

      try {
        await expect(copyToastReceipt({ message: 'Request failed' })).resolves.toBe(true)
        expect(writeText).toHaveBeenCalledWith('Request failed')
        expect(createElement).not.toHaveBeenCalled()
      } finally {
        createElement.mockRestore()
        Reflect.deleteProperty(navigator, 'clipboard')
      }
    })

    it.each([
      ['textarea creation', () => vi.spyOn(document, 'createElement').mockImplementation(() => { throw new Error('create failed') })],
      ['textarea append', () => vi.spyOn(document.body, 'appendChild').mockImplementation(() => { throw new Error('append failed') })],
      ['textarea selection', () => vi.spyOn(HTMLTextAreaElement.prototype, 'select').mockImplementation(() => { throw new Error('select failed') })],
      ['copy command', () => {
        Object.defineProperty(document, 'execCommand', {
          configurable: true,
          value: vi.fn(() => { throw new Error('copy failed') }),
        })
        return null
      }],
    ])('returns false without throwing when %s fails', async (_failure, setup) => {
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
      const mock = setup()

      try {
        await expect(copyToastReceipt({ message: 'Request failed' })).resolves.toBe(false)
        expect(document.querySelector('textarea')).toBeNull()
      } finally {
        if (mock) mock.mockRestore()
        Reflect.deleteProperty(document, 'execCommand')
        Reflect.deleteProperty(navigator, 'clipboard')
      }
    })

    it('does not throw when fallback cleanup fails', async () => {
      const execCommand = vi.fn().mockReturnValue(true)
      const remove = vi.spyOn(HTMLTextAreaElement.prototype, 'remove').mockImplementation(() => {
        throw new Error('cleanup failed')
      })
      Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })

      try {
        await expect(copyToastReceipt({ message: 'Request failed' })).resolves.toBe(true)
      } finally {
        remove.mockRestore()
        document.querySelector('textarea')?.remove()
        Reflect.deleteProperty(document, 'execCommand')
        Reflect.deleteProperty(navigator, 'clipboard')
      }
    })

    it('does not auto-remove a default error receipt', () => {
      vi.useFakeTimers()
      store.error('Persistent failure')

      vi.advanceTimersByTime(60_000)

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
