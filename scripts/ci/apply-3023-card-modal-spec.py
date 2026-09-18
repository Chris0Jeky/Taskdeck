from pathlib import Path

path = Path("frontend/taskdeck-web/src/tests/components/CardModal.spec.ts")
text = path.read_text(encoding="utf-8")

close_old = """      expect(wrapper.emitted('close')).toHaveLength(1)
      expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1')
      wrapper.unmount()
"""
close_new = """      expect(wrapper.emitted('close')).toHaveLength(1)
      expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', { intent: 'background' })
      wrapper.unmount()
"""
if text.count(close_old) != 2:
    raise SystemExit(f"recovery-close expectations: expected 2 matches, found {text.count(close_old)}")
text = text.replace(close_old, close_new)

kept_old = """      expect(titleValue(wrapper)).toBe(DRAFT)
      expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1')

      // The notice describes what is actually possible from here: no save, and
"""
kept_new = """      expect(titleValue(wrapper)).toBe(DRAFT)
      expect(mockStore.fetchBoard).toHaveBeenCalledWith('board-1', {
        intent: 'background',
        preserveCardComments: true,
      })

      // The notice describes what is actually possible from here: no save, and
"""
if text.count(kept_old) != 1:
    raise SystemExit(f"kept-open expectation: expected 1 match, found {text.count(kept_old)}")
text = text.replace(kept_old, kept_new)

path.write_text(text, encoding="utf-8")
