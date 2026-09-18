from pathlib import Path

path = Path("frontend/taskdeck-web/src/tests/components/CardModal.spec.ts")
text = path.read_text(encoding="utf-8")

old_import = "import { useSessionStore } from '../../store/sessionStore'\n"
new_import = (
    "import { useSessionStore } from '../../store/sessionStore'\n"
    "import { BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE } from '../../utils/boardLifecycleRefresh'\n"
)
if text.count(old_import) != 1:
    raise SystemExit(f"import anchor: expected 1, found {text.count(old_import)}")
text = text.replace(old_import, new_import, 1)

old_plain = "expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', { intent: 'background' })"
new_plain = """expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', {
        intent: 'background',
        backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
      })"""
if text.count(old_plain) != 2:
    raise SystemExit(f"ordinary refresh expectations: expected 2, found {text.count(old_plain)}")
text = text.replace(old_plain, new_plain)

old_preserve = """expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', {
        intent: 'background',
        preserveCardComments: true,
      })"""
new_preserve = """expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', {
        intent: 'background',
        backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
        preserveCardComments: true,
      })"""
if text.count(old_preserve) != 1:
    raise SystemExit(f"preserving refresh expectation: expected 1, found {text.count(old_preserve)}")
text = text.replace(old_preserve, new_preserve, 1)

path.write_text(text, encoding="utf-8")
