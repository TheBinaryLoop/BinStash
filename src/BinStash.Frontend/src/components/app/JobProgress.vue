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

const isGc = computed(() => props.job.jobType === 'ChunkStoreGc')

/**
 * Collection reports a phase rather than one counter, because its two long phases count
 * different things: mark walks releases, sweep and reclaim walk buckets. Showing a single
 * "x / y" across both would jump backwards at the phase change.
 */
const gc = computed(() => {
  const liveData = progress.value
  const stored = props.job.gcProgress

  return {
    phase: liveData?.gcPhase ?? stored?.phase ?? 'Pending',
    markedReleases: liveData?.markedReleases ?? stored?.markedReleases ?? 0,
    totalReleases: liveData?.totalReleases ?? stored?.totalReleases ?? 0,
    processedBuckets: liveData?.processedBuckets ?? stored?.processedBuckets ?? 0,
    totalBuckets: liveData?.totalBuckets ?? stored?.totalBuckets ?? 0,
    reachableObjects: liveData?.reachableObjects ?? stored?.reachableObjects ?? 0,
    quarantinedObjects: liveData?.quarantinedObjects ?? stored?.quarantinedObjects ?? 0,
    quarantinedBytes: liveData?.quarantinedBytes ?? stored?.quarantinedBytes ?? 0,
    reclaimedObjects: liveData?.reclaimedObjects ?? stored?.reclaimedObjects ?? 0,
    reclaimedBytes: liveData?.reclaimedBytes ?? stored?.reclaimedBytes ?? 0,
    packBytesDeleted: liveData?.packBytesDeleted ?? stored?.packBytesDeleted ?? 0,
    packsCompacted: liveData?.packsCompacted ?? stored?.packsCompacted ?? 0,
    resurrectedObjects: liveData?.resurrectedObjects ?? stored?.resurrectedObjects ?? 0,
    dryRun: liveData?.gcDryRun ?? stored?.dryRun ?? false,
  }
})

/**
 * Rebuilds count buckets, upgrades count releases. Prefer the live payload and fall
 * back to whatever the job record carried.
 */
const counters = computed(() => {
  const liveData = progress.value

  if (isGc.value) {
    // Mark is release-denominated, everything after it is bucket-denominated.
    const inMark = gc.value.phase === 'Mark'
    return inMark
      ? { unit: 'releases', processed: gc.value.markedReleases, total: gc.value.totalReleases, failed: 0 }
      : { unit: 'buckets', processed: gc.value.processedBuckets, total: gc.value.totalBuckets, failed: 0 }
  }

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
        <!-- The phase is the headline while a collection runs, because "Sweep" and "Reclaim"
             mean very different things for whether anything has been destroyed yet. Once the job
             reaches a terminal state the phase repeats it verbatim — a finished run rendered
             "Completed Completed" — so it is shown only when it adds something. On a failure it
             still does: it says which phase the run died in. -->
        <span v-if="isGc && gc.phase !== status" class="text-muted-foreground text-xs">
          {{ gc.phase }}
        </span>
        <Badge v-if="isGc && gc.dryRun" variant="outline" class="text-xs">dry run</Badge>
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

    <!-- A dry run counts what it WOULD collect without writing a tombstone, so labelling its
         figures "Quarantined" claims something that did not happen — the same ambiguity the
         completion log line had. Dropped and Freed are structurally zero in that mode, so they
         are omitted rather than shown as three zeros that look like a failed run. -->
    <dl v-if="isGc && gc.dryRun" class="grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-3">
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Unreachable</dt>
        <dd class="font-mono tabular-nums">{{ formatBytes(gc.quarantinedBytes) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Objects</dt>
        <dd class="font-mono tabular-nums">{{ formatNumber(gc.quarantinedObjects) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Still reachable</dt>
        <dd class="font-mono tabular-nums">{{ formatNumber(gc.reachableObjects) }}</dd>
      </div>
    </dl>

    <!-- Collection's outcome is two numbers, not one, and conflating them would overstate
         what a run achieved: quarantined content is hidden but still on disk, and only the
         pack bytes deleted have actually returned to the volume. -->
    <dl v-else-if="isGc" class="grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-4">
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Quarantined</dt>
        <dd class="font-mono tabular-nums">{{ formatBytes(gc.quarantinedBytes) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Dropped</dt>
        <dd class="font-mono tabular-nums">{{ formatBytes(gc.reclaimedBytes) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Freed on disk</dt>
        <dd class="font-mono tabular-nums">{{ formatBytes(gc.packBytesDeleted) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-muted-foreground">Objects</dt>
        <dd class="font-mono tabular-nums">{{ formatNumber(gc.quarantinedObjects) }}</dd>
      </div>
    </dl>

    <!-- The mark phase's coverage, which is what makes the figures above trustworthy: the run
         aborts rather than under-marking, so "all releases walked" is the evidence that nothing
         was called unreachable merely because it could not be resolved. -->
    <p v-if="isGc && gc.totalReleases > 0" class="text-muted-foreground text-xs">
      Walked {{ formatNumber(gc.markedReleases) }} of {{ formatNumber(gc.totalReleases) }}
      release<template v-if="gc.totalReleases !== 1">s</template>
      <template v-if="gc.dryRun"> · nothing was quarantined or destroyed</template>
    </p>

    <p v-if="isGc && gc.resurrectedObjects > 0" class="text-warning text-xs">
      {{ formatNumber(gc.resurrectedObjects) }} object(s) were taken back by an in-flight upload —
      if this keeps happening, the quarantine retention is too short for how long uploads run here.
    </p>

    <div v-if="!isGc" class="text-muted-foreground flex gap-4 text-xs">
      <span v-if="counters.failed > 0" class="text-destructive">
        {{ formatNumber(counters.failed) }} failed
      </span>
      <span v-if="bytesSaved > 0">{{ formatBytes(bytesSaved) }} reclaimed</span>
    </div>
  </div>
</template>
