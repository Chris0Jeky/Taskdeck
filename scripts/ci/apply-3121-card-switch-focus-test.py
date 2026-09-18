from pathlib import Path

path = Path("frontend/taskdeck-web/src/tests/components/CardModalPermissionRecovery.spec.ts")
text = path.read_text(encoding="utf-8")
anchor = """  it('restores focus after retrying type permission from the mounted card modal form', async () => {
"""
if text.count(anchor) != 1:
    raise SystemExit(f"expected one focus-test anchor, found {text.count(anchor)}")

addition = r'''  it('does not let an in-flight permission retry steal focus after the selected card changes', async () => {
    store.currentBoard = board(undefined)
    const staleRetry = deferred<BoardDetail>()
    vi.mocked(boardsApi.getBoard)
      .mockRejectedValueOnce({ response: { status: 500 } })
      .mockReturnValueOnce(staleRetry.promise)

    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels: [] },
      attachTo: document.body,
    })
    await flushPromises()

    const refresh = wrapper.get('[data-testid="card-type-permission-refresh"]')
    ;(refresh.element as HTMLButtonElement).focus()
    await refresh.trigger('click')
    await flushPromises()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(2)

    const replacement: Card = {
      ...card,
      id: 'card-2',
      title: 'Replacement card',
      updatedAt: 'v2',
    }
    store.currentBoard = board(true)
    await wrapper.setProps({ card: replacement })
    await flushPromises()

    const close = wrapper.get('[aria-label="Close card editor"]')
    expect(document.activeElement).toBe(close.element)

    // The obsolete request may still settle at the adapter boundary. It must
    // not revive card A's focus claim inside card B's form.
    staleRetry.resolve(board(true))
    await flushPromises()
    expect(document.activeElement).toBe(close.element)
    wrapper.unmount()
  })

'''
path.write_text(text.replace(anchor, addition + anchor, 1), encoding="utf-8")
