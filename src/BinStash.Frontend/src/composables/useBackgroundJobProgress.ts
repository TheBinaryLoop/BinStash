import { ref, readonly, onUnmounted, type Ref } from 'vue'
import { apolloClient } from '@/shared/api/apolloClient'
import gql from 'graphql-tag'
import type { Subscription as ZenSubscription } from 'zen-observable-ts'

export type BackgroundJobProgress = {
  jobId: string
  jobType: string
  status: string
  // Release-upgrade progress
  totalReleases: number
  processedReleases: number
  failedReleases: number
  skippedReleases: number
  bytesSaved: number
  bytesGrown: number
  // Chunk-store rebuild progress
  totalBuckets: number
  processedBuckets: number
  failedBuckets: number
  chunkStoreId?: string | null
  startedAt?: string | null
  completedAt?: string | null
}

const BACKGROUND_JOB_PROGRESS_SUBSCRIPTION = gql`
  subscription BackgroundJobProgress($jobId: UUID!) {
    backgroundJobProgress(jobId: $jobId) {
      jobId
      jobType
      status
      totalReleases
      processedReleases
      failedReleases
      skippedReleases
      bytesSaved
      bytesGrown
      totalBuckets
      processedBuckets
      failedBuckets
      chunkStoreId
      startedAt
      completedAt
    }
  }
`

/**
 * Composable that subscribes to real-time progress updates for a background job
 * via GraphQL WebSocket subscriptions.
 *
 * Call `subscribe(jobId)` to start listening; call `unsubscribe()` or let the
 * component unmount to stop. The reactive `progress` ref is updated on every
 * event from the server.
 */
export function useBackgroundJobProgress() {
  const progress = ref<BackgroundJobProgress | null>(null) as Ref<BackgroundJobProgress | null>
  const error = ref<string | null>(null)
  const isSubscribed = ref(false)

  let subscription: ZenSubscription | null = null

  function subscribe(jobId: string) {
    unsubscribe()

    error.value = null
    isSubscribed.value = true

    const observable = apolloClient.subscribe({
      query: BACKGROUND_JOB_PROGRESS_SUBSCRIPTION,
      variables: { jobId },
    })

    subscription = observable.subscribe({
      next(result) {
        if (result.data?.backgroundJobProgress) {
          progress.value = result.data.backgroundJobProgress as BackgroundJobProgress

          // Auto-unsubscribe when the job reaches a terminal state
          const status = progress.value.status
          if (status === 'Completed' || status === 'Failed' || status === 'Cancelled') {
            unsubscribe()
          }
        }
      },
      error(err) {
        error.value = err instanceof Error ? err.message : 'Subscription failed.'
        isSubscribed.value = false
      },
      complete() {
        isSubscribed.value = false
      },
    })
  }

  function unsubscribe() {
    if (subscription) {
      subscription.unsubscribe()
      subscription = null
    }
    isSubscribed.value = false
  }

  onUnmounted(unsubscribe)

  return {
    progress: readonly(progress),
    error: readonly(error),
    isSubscribed: readonly(isSubscribed),
    subscribe,
    unsubscribe,
  }
}
