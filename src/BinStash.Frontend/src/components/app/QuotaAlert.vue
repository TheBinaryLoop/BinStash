<script setup lang="ts">
import { Ban, TriangleAlert } from '@lucide/vue'
import { computed } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

import { evaluateQuota, type QuotaUsage } from '@/lib/quota'
import { formatBytes } from '@/lib/format'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'

/**
 * The workspace's standing quota warning. Renders nothing while there is nothing to say, so it can
 * sit unconditionally at the top of any page that has the usage figures — which is the point: a
 * workspace whose uploads have started failing should find out on the page it lands on, not only
 * on the one page it would have to know to visit.
 */
const props = defineProps<{
  usage?: QuotaUsage | null
  /** Where "View usage" goes. Omitted on the usage page itself, which is already there. */
  detailsTo?: RouteLocationRaw
}>()

const status = computed(() => evaluateQuota(props.usage))

const variant = computed(() => (status.value.level === 'approaching' ? 'warning' : 'destructive'))

const icon = computed(() => (status.value.level === 'blocked' ? Ban : TriangleAlert))
</script>

<template>
  <Alert v-if="status.notable" :variant="variant">
    <component :is="icon" />
    <AlertTitle>{{ status.title }}</AlertTitle>
    <AlertDescription>
      <span>{{ status.description }}</span>
      <span v-if="status.remainingBytes !== null && status.level !== 'blocked'" class="block">
        <template v-if="status.remainingBytes > 0">
          {{ formatBytes(status.remainingBytes) }} left.
        </template>
        <template v-else>No headroom left.</template>
        <RouterLink
          v-if="detailsTo"
          :to="detailsTo"
          class="underline underline-offset-2 hover:no-underline"
        >
          View usage
        </RouterLink>
      </span>
    </AlertDescription>
  </Alert>
</template>
