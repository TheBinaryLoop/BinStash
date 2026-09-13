<script setup lang="ts">
import { computed } from 'vue'

import { Badge } from '@/components/ui/badge'
import { useSubscription } from '@/composables/useGraphql'
import { BackgroundJobProgressDocument } from '@/graphql/generated'
import type { BackgroundJobSummaryFragment } from '@/graphql/generated'
import { formatBytes, formatNumber } from '@/lib/format'

const props = defineProps<{ job: BackgroundJobSummaryFragment }>()

/**
 * Only hold the websocket open while the job can still change. A finished job's
 * numbers come from the query that listed it.
 */
const live = computed(() => props.job.status === 'Running' || props.job.status === 'Pending')

const { result } = useSubscription(
  BackgroundJobProgressDocument,
  () => ({ jobId: props.job.id }),
  { enabled: live },
)

const progress = computed(() => result.value?.backgroundJobProgress)

const status = computed(() => progress.value?.status ?? props.job.status)

/**
 * Rebuilds count buckets, upgrades count releases. Prefer the live payload and fall
 * back to whatever the job record carried.
 */
const counters = computed(() => {
  const liveData = progress.value

  const buckets = {
    processed: liveData?.processedBuckets ?? props.job.rebuildProgress?.processedBuckets ?? 0,
    total: liveData?.totalBuckets ?? props.job.rebuildProgress?.totalBuckets ?? 0,
    failed: liveData?.failedBuckets ?? props.job.rebuildProgress?.failedBuckets ?? 0,
  }
  const releases = {
    processed: liveData?.processedReleases ?? props.job.upgradeProgress?.processedReleases ?? 0,
    total: liveData?.totalReleases ?? props.job.upgradeProgress?.totalReleases ?? 0,
    failed: liveData?.failedReleases ?? props.job.upgradeProgress?.failedReleases ?? 0,
  }

  return releases.total > 0
    ? { unit: 'releases', ...releases }
    : { unit: 'buckets', ...buckets }
})

const fraction = computed(() =>
  counters.value.total > 0 ? Math.min(counters.value.processed / counters.value.total, 1) : 0,
)

const bytesSaved = computed(
  () => progress.value?.bytesSaved ?? props.job.upgradeProgress?.bytesSaved ?? 0,
)

const tone = computed(() => {
  if (status.value === 'Failed') return 'bg-destructive'
  if (status.value === 'Completed') return 'bg-success'
  return 'bg-primary'
})
</script>

<template>
  <div class="space-y-2">
    <div class="flex items-baseline justify-between gap-3">
      <div class="flex items-center gap-2">
        <Badge
          :variant="status === 'Failed' ? 'destructive' : 'secondary'"
          class="text-xs"
        >
          {{ status }}
        </Badge>
        <span v-if="live" class="text-muted-foreground text-xs">live</span>
      </div>
      <span class="text-muted-foreground font-mono text-xs tabular-nums">
        {{ formatNumber(counters.processed) }} / {{ formatNumber(counters.total) }}
        {{ counters.unit }}
      </span>
    </div>

    <div
      class="bg-muted h-1.5 overflow-hidden rounded-full"
      role="progressbar"
      :aria-valuenow="Math.round(fraction * 100)"
      aria-valuemin="0"
      aria-valuemax="100"
    >
      <div
        class="h-full rounded-full transition-[width] duration-500"
        :class="tone"
        :style="{ width: `${fraction * 100}%` }"
      />
    </div>

    <div class="text-muted-foreground flex gap-4 text-xs">
      <span v-if="counters.failed > 0" class="text-destructive">
        {{ formatNumber(counters.failed) }} failed
      </span>
      <span v-if="bytesSaved > 0">{{ formatBytes(bytesSaved) }} reclaimed</span>
    </div>
  </div>
</template>
