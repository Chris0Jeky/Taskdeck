from pathlib import Path

path = Path("frontend/taskdeck-web/src/components/board/card-modal/CardModalForm.vue")
text = path.read_text(encoding="utf-8")

old_ref = """const typePermissionRefresh = ref<HTMLButtonElement | null>(null)
const retryOwnedFocus = ref(false)

watch(
"""
new_ref = """const typePermissionRefresh = ref<HTMLButtonElement | null>(null)
const retryOwnedCardId = ref<string | null>(null)

// A replacement card owns a separate focus contract. Retire the old retry's
// claim synchronously before parent/card permission watchers can settle it.
watch(
  () => props.card.id,
  () => { retryOwnedCardId.value = null },
  { flush: 'sync' },
)

watch(
"""
if text.count(old_ref) != 1:
    raise SystemExit(f"focus owner declaration: expected 1, found {text.count(old_ref)}")
text = text.replace(old_ref, new_ref, 1)

old_start = """    if (checking && !wasChecking) {
      retryOwnedFocus.value = document.activeElement === typePermissionRefresh.value
      return
    }
"""
new_start = """    if (checking && !wasChecking) {
      retryOwnedCardId.value = document.activeElement === typePermissionRefresh.value
        ? props.card.id
        : null
      return
    }
"""
if text.count(old_start) != 1:
    raise SystemExit(f"retry start ownership: expected 1, found {text.count(old_start)}")
text = text.replace(old_start, new_start, 1)

old_restore = """    const activeElement = document.activeElement
    const shouldRestoreFocus = activeElement === typePermissionRefresh.value
      || (retryOwnedFocus.value && (activeElement === document.body || activeElement === null))
    retryOwnedFocus.value = false
    if (!canEdit || !shouldRestoreFocus) return
"""
new_restore = """    const activeElement = document.activeElement
    const retryOwnsCurrentCard = retryOwnedCardId.value === props.card.id
    const shouldRestoreFocus = retryOwnsCurrentCard && (
      activeElement === typePermissionRefresh.value
      || activeElement === document.body
      || activeElement === null
    )
    retryOwnedCardId.value = null
    if (!canEdit || !shouldRestoreFocus) return
"""
if text.count(old_restore) != 1:
    raise SystemExit(f"retry focus restoration: expected 1, found {text.count(old_restore)}")
text = text.replace(old_restore, new_restore, 1)

path.write_text(text, encoding="utf-8")
