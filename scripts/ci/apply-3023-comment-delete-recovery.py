from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


path = Path("frontend/taskdeck-web/src/components/board/CardModal.vue")
text = path.read_text(encoding="utf-8")

text = replace_once(
    text,
    """const permissionRecoveryRefresh = ref<HTMLButtonElement | null>(null)
const permissionRetryOwnedFocus = ref(false)
""",
    """const permissionRecoveryRefresh = ref<HTMLButtonElement | null>(null)
const commentDeleteCancel = ref<HTMLButtonElement | null>(null)
const commentDeletePermissionRefresh = ref<HTMLButtonElement | null>(null)
const permissionRetryOwnedFocus = ref(false)
const permissionRecoveryMessage = computed(() => {
  if (typePermissionChecking.value) {
    return 'Checking current board access. Your unsaved changes are kept.'
  }
  if (accessUnavailable.value) {
    return 'This board is no longer available to this editor. Your unsaved changes are kept. Ask a board admin to check your access, then refresh permission.'
  }
  if (typePermissionUnknown.value) {
    return 'Could not confirm current board permission. Editing stays locked. Your unsaved changes are kept; refresh permission to try again.'
  }
  if (!boardCanWrite.value) {
    return 'This board is read-only for you. Your unsaved changes are kept. Ask a board admin to restore write access, then refresh permission.'
  }
  return 'Board write permission confirmed. Your unsaved changes are kept.'
})
""",
    "permission recovery refs and message",
)

text = replace_once(
    text,
    """} = useCardModal({
  getCard: () => props.card,
  getIsOpen: () => props.isOpen,
  getLabels: () => props.labels,
  onUpdated: () => emit('updated'),
  onClose: () => emit('close'),
  onPermissionDenied: recoverFromPermissionDenied,
})

watch(hasUnsavedChanges, (dirty) => {
""",
    """} = useCardModal({
  getCard: () => props.card,
  getIsOpen: () => props.isOpen,
  getLabels: () => props.labels,
  onUpdated: () => emit('updated'),
  onClose: () => emit('close'),
  onPermissionDenied: recoverFromPermissionDenied,
})

watch(
  [showCommentDeleteConfirm, permissionRecovery, editorWritesBlocked, typePermissionChecking],
  async ([open, recovering, blocked, checking]) => {
    if (!open || !recovering || !blocked) return

    await nextTick()
    if (!showCommentDeleteConfirm.value || !permissionRecovery.value || !editorWritesBlocked.value) return

    const active = document.activeElement
    const activeIsDisabledButton = active instanceof HTMLButtonElement && active.disabled
    if (
      active instanceof HTMLElement &&
      active !== document.body &&
      active.isConnected &&
      !activeIsDisabledButton
    ) {
      return
    }

    const target = checking
      ? commentDeleteCancel.value
      : commentDeletePermissionRefresh.value ?? commentDeleteCancel.value
    target?.focus()
  },
)

watch(hasUnsavedChanges, (dirty) => {
""",
    "comment delete recovery focus watch",
)

text = replace_once(
    text,
    """        <div v-if="permissionRecovery" class="my-3 space-y-2 text-sm" data-testid="card-permission-recovery">
          <p role="status">
            <template v-if="typePermissionChecking">Checking current board access. Your unsaved changes are kept.</template>
            <template v-else-if="accessUnavailable">This board is no longer available to this editor. Your unsaved changes are kept. Ask a board admin to check your access, then refresh permission.</template>
            <template v-else-if="typePermissionUnknown">Could not confirm current board permission. Editing stays locked. Your unsaved changes are kept; refresh permission to try again.</template>
            <template v-else-if="!boardCanWrite">This board is read-only for you. Your unsaved changes are kept. Ask a board admin to restore write access, then refresh permission.</template>
            <template v-else>Board write permission confirmed. Your unsaved changes are kept.</template>
          </p>
          <button ref="permissionRecoveryRefresh" type="button" data-testid="card-permission-refresh" :disabled="typePermissionChecking" @click="refreshTypePermission">Refresh board permission</button>
        </div>
""",
    """        <div v-if="permissionRecovery && !showCommentDeleteConfirm" class="my-3 space-y-2 text-sm" data-testid="card-permission-recovery">
          <p role="status">{{ permissionRecoveryMessage }}</p>
          <button ref="permissionRecoveryRefresh" type="button" data-testid="card-permission-refresh" :disabled="typePermissionChecking" @click="refreshTypePermission">Refresh board permission</button>
        </div>
""",
    "outer permission recovery presentation",
)

text = replace_once(
    text,
    """  <TdDialog
    :open="showCommentDeleteConfirm"
    :title="t('cardModal.commentDelete.title')"
    :description="t('cardModal.commentDelete.description')"
    :close-on-backdrop="!isDeletingComment"
    @close="handleCommentDeleteCancel"
  >
    <template #footer>
      <button
        type="button"
        :disabled="isDeletingComment"
""",
    """  <TdDialog
    :open="showCommentDeleteConfirm"
    :title="t('cardModal.commentDelete.title')"
    :description="t('cardModal.commentDelete.description')"
    :close-on-backdrop="!isDeletingComment"
    @close="handleCommentDeleteCancel"
  >
    <div
      v-if="permissionRecovery"
      class="space-y-2 text-sm"
      data-testid="card-comment-delete-permission-recovery"
    >
      <p role="status">{{ permissionRecoveryMessage }}</p>
      <button
        ref="commentDeletePermissionRefresh"
        type="button"
        data-testid="card-comment-delete-permission-refresh"
        :disabled="typePermissionChecking"
        @click="refreshTypePermission"
      >
        Refresh board permission
      </button>
    </div>
    <template #footer>
      <button
        ref="commentDeleteCancel"
        type="button"
        :disabled="isDeletingComment"
""",
    "comment delete in-dialog recovery",
)

path.write_text(text, encoding="utf-8")
