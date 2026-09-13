<script setup lang="ts">
import { TriangleAlert } from '@lucide/vue'

import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { errorMessage } from '@/lib/errors'

defineProps<{
  loading?: boolean
  error?: unknown
  /** Suppresses the skeleton when cached data is already on screen. */
  hasData?: boolean
  skeletonRows?: number
}>()

const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    v-if="error && !hasData"
    class="border-destructive/30 bg-destructive/5 flex flex-col items-center gap-3 rounded-lg border px-6 py-10 text-center"
  >
    <TriangleAlert class="text-destructive size-5" />
    <p class="text-sm">{{ errorMessage(error) }}</p>
    <Button variant="outline" size="sm" @click="emit('retry')">Retry</Button>
  </div>

  <div v-else-if="loading && !hasData" class="space-y-3">
    <Skeleton v-for="row in skeletonRows ?? 4" :key="row" class="h-12 w-full" />
  </div>

  <slot v-else />
</template>
