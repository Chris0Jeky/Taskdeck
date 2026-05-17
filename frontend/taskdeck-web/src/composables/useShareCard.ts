import { computed } from 'vue'
import type { Card } from '../types/board'

export function useShareCard() {
  const canShare = computed(() => typeof navigator !== 'undefined' && 'share' in navigator)

  async function shareCard(card: Card): Promise<boolean> {
    if (!canShare.value) return false

    const shareData: ShareData = {
      title: card.title,
      text: card.description || undefined,
    }

    try {
      await navigator.share(shareData)
      return true
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return false
      }
      throw error
    }
  }

  return { canShare, shareCard }
}
