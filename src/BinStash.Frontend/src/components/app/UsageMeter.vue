<script setup lang="ts">
import { computed } from 'vue'

import { formatBytes, formatPercent } from '@/lib/format'

const props = defineProps<{
  used: number
  limit?: number | null
  isLimited: boolean
}>()

const fraction = computed(() =>
  props.isLimited && props.limit ? Math.min(props.used / props.limit, 1) : 0,
)

/**
 * Colour carries the warning, so the thresholds have to mean something: amber once a
 * top-up is worth planning, red once ingest is about to start failing.
 */
const tone = computed(() => {
  if (!props.isLimited) return 'bg-primary'
  if (fraction.value >= 0.95) return 'bg-destructive'
  if (fraction.value >= 0.8) return 'bg-warning'
  return 'bg-primary'
})

const overage = computed(() => props.isLimited && props.limit != null && props.used > props.limit)
</script>

<template>
  <div class="space-y-2">
    <div class="flex items-baseline justify-between gap-4">
      <span class="font-mono text-sm tabular-nums">{{ formatBytes(used) }}</span>
      <span class="text-muted-foreground text-xs">
        <template v-if="isLimited && limit != null">
          of {{ formatBytes(limit) }} · {{ formatPercent(used / limit, 0) }}
        </template>
        <template v-else>No plan limit</template>
      </span>
    </div>

    <!-- Only draw a bar when there is something to fill. With no quota there is no
         denominator, and a full-width bar reads as "you are at capacity". -->
    <div
      v-if="isLimited"
      class="bg-muted h-1.5 w-full overflow-hidden rounded-full"
      role="progressbar"
      :aria-valuenow="Math.round(fraction * 100)"
      aria-valuemin="0"
      aria-valuemax="100"
      aria-label="Storage quota used"
    >
      <div
        class="h-full rounded-full transition-[width] duration-500"
        :class="tone"
        :style="{ width: `${fraction * 100}%` }"
      />
    </div>

    <div
      v-else
      class="bg-muted/60 h-1.5 w-full overflow-hidden rounded-full"
      aria-hidden="true"
    >
      <div class="bg-primary/35 h-full w-full rounded-full [mask-image:repeating-linear-gradient(90deg,#000_0_6px,transparent_6px_12px)]" />
    </div>

    <p v-if="overage" class="text-destructive text-xs">
      Over the plan limit — new uploads are rejected until usage drops or the plan changes.
    </p>
  </div>
</template>
