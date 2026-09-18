from pathlib import Path


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one {label} replacement, found {count}")
    return source.replace(old, new, 1)


modal_state_path = Path("frontend/taskdeck-web/src/composables/useCardModal.ts")
modal_state = modal_state_path.read_text(encoding="utf-8")
modal_state = replace_once(
    modal_state,
    "  return {\n    acceptAssignmentVersion: (updatedAt: string, previousVersion?: string) => {\n",
    "  return {\n"
    "    acceptCommittedWriteVersion: (updatedAt: string) => {\n"
    "      // A matching lifecycle receipt advances only the CAS baseline.\n"
    "      // It must never replace a newer local form draft.\n"
    "      expectedUpdatedAt.value = updatedAt\n"
    "    },\n"
    "    acceptAssignmentVersion: (updatedAt: string, previousVersion?: string) => {\n",
    "useCardModal return seam",
)
modal_state_path.write_text(modal_state, encoding="utf-8", newline="\n")

assignment_path = Path("frontend/taskdeck-web/src/components/board/CardAssignmentField.vue")
assignment = assignment_path.read_text(encoding="utf-8")
assignment = replace_once(
    assignment,
    "const props = defineProps<{ card: Card; readOnly: boolean; readsBlocked?: boolean; disabled?: boolean }>()",
    "const props = defineProps<{\n"
    "  card: Card\n"
    "  committedCard?: Card | null\n"
    "  readOnly: boolean\n"
    "  readsBlocked?: boolean\n"
    "  disabled?: boolean\n"
    "}>()",
    "CardAssignmentField props",
)
assignment = replace_once(
    assignment,
    "function reset(card: Card) {\n"
    "  displayedAssignments.value = card.assignments ?? []\n"
    "  archived.value = !!card.isArchived\n"
    "  baseline.value = (card.assignments ?? []).map(a => a.userId)\n"
    "  selected.value = [...baseline.value]\n"
    "  version.value = card.updatedAt\n"
    "}\n"
    "async function load(refresh = false) {\n",
    "function reset(card: Card) {\n"
    "  displayedAssignments.value = card.assignments ?? []\n"
    "  archived.value = !!card.isArchived\n"
    "  baseline.value = (card.assignments ?? []).map(a => a.userId)\n"
    "  selected.value = [...baseline.value]\n"
    "  version.value = card.updatedAt\n"
    "}\n"
    "// A late archive/restore receipt owns lifecycle and the next CAS token only.\n"
    "// Preserve the assignment baseline and local selection so a newer draft is not overwritten.\n"
    "watch(\n"
    "  () => props.committedCard,\n"
    "  committed => {\n"
    "    if (!committed || committed.boardId !== props.card.boardId || committed.id !== props.card.id) return\n"
    "    version.value = committed.updatedAt\n"
    "    archived.value = !!committed.isArchived\n"
    "  },\n"
    "  { flush: 'sync' },\n"
    ")\n"
    "async function load(refresh = false) {\n",
    "CardAssignmentField reset seam",
)
assignment_path.write_text(assignment, encoding="utf-8", newline="\n")

modal_path = Path("frontend/taskdeck-web/src/components/board/CardModal.vue")
modal = modal_path.read_text(encoding="utf-8")
modal = replace_once(
    modal,
    "  committedArchiveCard.value = committed\n"
    "  archiveStateAfterChange.value = committed.isArchived === true\n"
    "  if (hasUnsavedChanges.value) archiveCompletedWithDraft.value = true\n",
    "  committedArchiveCard.value = committed\n"
    "  archiveStateAfterChange.value = committed.isArchived === true\n"
    "  acceptCommittedWriteVersion(committed.updatedAt)\n"
    "  if (hasUnsavedChanges.value) archiveCompletedWithDraft.value = true\n",
    "inactive lifecycle receipt seam",
)
modal = replace_once(
    modal,
    "  hasUnsavedChanges: hasCardUnsavedChanges,\n"
    "  acceptAssignmentVersion,\n"
    "  isSaving,\n",
    "  hasUnsavedChanges: hasCardUnsavedChanges,\n"
    "  acceptCommittedWriteVersion,\n"
    "  acceptAssignmentVersion,\n"
    "  isSaving,\n",
    "CardModal composable destructuring",
)
modal = replace_once(
    modal,
    "        <CardAssignmentField v-if=\"isOpen\" :card=\"card\" :disabled=\"isSaving\"\n"
    "          :read-only=\"!boardCanWrite || cardIsArchived\"\n",
    "        <CardAssignmentField v-if=\"isOpen\" :card=\"card\" :committed-card=\"committedArchiveCard\" :disabled=\"isSaving\"\n"
    "          :read-only=\"!boardCanWrite || cardIsArchived\"\n",
    "CardAssignmentField template seam",
)
modal_path.write_text(modal, encoding="utf-8", newline="\n")

Path(".github/workflows/pr-3188-branch-patch.yml").unlink()
Path("scripts/ci/pr3188_patch.py").unlink()
