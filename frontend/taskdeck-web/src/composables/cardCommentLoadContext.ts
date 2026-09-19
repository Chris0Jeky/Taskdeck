import type { InjectionKey, Ref } from 'vue'

/**
 * Per-editor ownership for the card-comment read. The modal composable owns the
 * request lifecycle; the comments surface consumes it without coupling the
 * reusable component to the board store or duplicating the request.
 */
export interface CardCommentLoadContext {
  loading: Readonly<Ref<boolean>>
  error: Readonly<Ref<string | null>>
  retry: () => void
}

export const cardCommentLoadContextKey: InjectionKey<CardCommentLoadContext> = Symbol(
  'card-comment-load-context',
)
