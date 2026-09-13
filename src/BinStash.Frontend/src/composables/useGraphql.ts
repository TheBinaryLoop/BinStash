import type { TypedDocumentNode } from '@graphql-typed-document-node/core'
import type { ErrorPolicy, OperationVariables, WatchQueryFetchPolicy } from '@apollo/client/core'
import { onScopeDispose, readonly, ref, shallowRef, toValue, watch } from 'vue'
import type { MaybeRefOrGetter, Ref } from 'vue'

import { apolloClient } from '@/lib/apollo'

/**
 * Minimal Vue bindings over Apollo Client 4.
 *
 * `@vue/apollo-composable` is deliberately NOT used: its stable line (4.2.2) peers
 * `@apollo/client: ^3.4.13` and imports `isApolloError`, which Apollo 4 removed, so it
 * fails to bundle. Apollo 4 support exists only in a `5.0.0-alpha` prerelease, which is
 * not something to ship in an enterprise product. These three composables are the entire
 * surface the app needs, and owning them keeps us on Apollo 4 + graphql 17.
 */

interface UseQueryOptions {
  /** Skip the query until this turns true — for dependent queries and lazy tabs. */
  enabled?: MaybeRefOrGetter<boolean>
  fetchPolicy?: WatchQueryFetchPolicy
  errorPolicy?: ErrorPolicy
}

export interface UseQueryReturn<TData, TVars> {
  result: Readonly<Ref<TData | undefined>>
  loading: Readonly<Ref<boolean>>
  error: Readonly<Ref<unknown>>
  refetch: (variables?: Partial<TVars>) => Promise<void>
  fetchMore: (variables: Partial<TVars>) => Promise<void>
}

export function useQuery<TData, TVars extends OperationVariables>(
  document: TypedDocumentNode<TData, TVars>,
  variables?: MaybeRefOrGetter<TVars>,
  options: UseQueryOptions = {},
): UseQueryReturn<TData, TVars> {
  const result = shallowRef<TData | undefined>()
  const loading = ref(false)
  const error = shallowRef<unknown>(undefined)

  let observable: ReturnType<typeof apolloClient.watchQuery<TData, TVars>> | null = null
  let subscription: { unsubscribe(): void } | null = null

  function stop() {
    subscription?.unsubscribe()
    subscription = null
    observable = null
  }

  function start(vars: TVars) {
    stop()
    loading.value = true

    observable = apolloClient.watchQuery<TData, TVars>({
      query: document,
      variables: vars,
      fetchPolicy: options.fetchPolicy,
      errorPolicy: options.errorPolicy,
    })

    subscription = observable.subscribe({
      next(next) {
        // Apollo 4 makes `data` optional and adds a `dataState` discriminant; keep the
        // previous value rather than flashing empty while a background refetch is in flight.
        if (next.data !== undefined) result.value = next.data
        loading.value = next.loading
        error.value = next.error
      },
      error(caught: unknown) {
        error.value = caught
        loading.value = false
      },
    })
  }

  watch(
    () => ({
      enabled: options.enabled === undefined ? true : toValue(options.enabled),
      variables: (variables ? toValue(variables) : {}) as TVars,
    }),
    ({ enabled, variables: vars }) => {
      if (!enabled) {
        stop()
        loading.value = false
        return
      }
      start(vars)
    },
    { immediate: true, deep: true },
  )

  onScopeDispose(stop)

  return {
    result: readonly(result) as Readonly<Ref<TData | undefined>>,
    loading: readonly(loading),
    error: readonly(error) as Readonly<Ref<unknown>>,
    async refetch(next?: Partial<TVars>) {
      if (!observable) return
      loading.value = true
      try {
        await observable.refetch(next as TVars | undefined)
      } finally {
        loading.value = false
      }
    },
    async fetchMore(next: Partial<TVars>) {
      if (!observable) return
      await observable.fetchMore({ variables: next as TVars })
    },
  }
}

export interface UseMutationReturn<TData, TVars> {
  mutate: (variables: TVars) => Promise<TData | undefined>
  loading: Readonly<Ref<boolean>>
  error: Readonly<Ref<unknown>>
}

export function useMutation<TData, TVars extends OperationVariables>(
  document: TypedDocumentNode<TData, TVars>,
  options: { refetchQueries?: string[]; awaitRefetchQueries?: boolean } = {},
): UseMutationReturn<TData, TVars> {
  const loading = ref(false)
  const error = shallowRef<unknown>(undefined)

  return {
    loading: readonly(loading),
    error: readonly(error) as Readonly<Ref<unknown>>,
    async mutate(variables: TVars) {
      loading.value = true
      error.value = undefined

      try {
        const response = await apolloClient.mutate<TData, TVars>({
          mutation: document,
          variables,
          refetchQueries: options.refetchQueries,
          awaitRefetchQueries: options.awaitRefetchQueries ?? true,
        })
        return response.data ?? undefined
      } catch (caught) {
        error.value = caught
        // Rethrown on purpose: callers decide between a toast, inline errors, or both.
        throw caught
      } finally {
        loading.value = false
      }
    },
  }
}

export function useSubscription<TData, TVars extends OperationVariables>(
  document: TypedDocumentNode<TData, TVars>,
  variables: MaybeRefOrGetter<TVars>,
  options: { enabled?: MaybeRefOrGetter<boolean> } = {},
) {
  const result = shallowRef<TData | undefined>()
  const error = shallowRef<unknown>(undefined)
  let subscription: { unsubscribe(): void } | null = null

  function stop() {
    subscription?.unsubscribe()
    subscription = null
  }

  watch(
    () => ({
      enabled: options.enabled === undefined ? true : toValue(options.enabled),
      variables: toValue(variables),
    }),
    ({ enabled, variables: vars }) => {
      stop()
      if (!enabled) return

      subscription = apolloClient
        .subscribe<TData, TVars>({ query: document, variables: vars })
        .subscribe({
          next(next) {
            if (next.data) result.value = next.data
          },
          error(caught: unknown) {
            error.value = caught
          },
        })
    },
    { immediate: true, deep: true },
  )

  onScopeDispose(stop)

  return { result: readonly(result) as Readonly<Ref<TData | undefined>>, error }
}
