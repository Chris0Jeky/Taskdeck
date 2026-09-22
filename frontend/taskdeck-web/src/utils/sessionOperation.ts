/** Local supersession does not undo a request already accepted by the server. */
export class SessionOperationSupersededError extends Error {
  constructor(serverCompleted = false) {
    super('This session operation was superseded by a newer sign-in or sign-out.' +
      (serverCompleted ? ' The server request completed; its result was not installed in this browser.' : ''))
    this.name = 'SessionOperationSupersededError'
  }
}
