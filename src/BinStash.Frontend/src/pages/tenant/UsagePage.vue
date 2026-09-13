<script setup lang="ts">
import { Ban, CircleCheck, HardDrive, Layers, Package, TrendingDown } from '@lucide/vue'
import { computed } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import UsageMeter from '@/components/app/UsageMeter.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { useQuery } from '@/composables/useGraphql'
import { TenantUsageDocument } from '@/graphql/generated'
import { formatBytes, formatNumber, formatPercent } from '@/lib/format'

const { result, loading, error, refetch } = useQuery(TenantUsageDocument, {})
const usage = computed(() => result.value?.tenantUsage)

const reclaimed = computed(() =>
  usage.value ? usage.value.deduplicationSavedBytes + usage.value.compressionSavedBytes : 0,
)

/** How much smaller the stored footprint is than what users uploaded. */
const overallReduction = computed(() => {
  const stats = usage.value
  if (!stats || stats.logicalBytes <= 0) return null
  return 1 - stats.compressedBytes / stats.logicalBytes
})

const entitlements = computed(() => {
  const stats = usage.value
  if (!stats) return []
  return [
    { label: 'Uploads', allowed: stats.isIngestAllowed },
    { label: 'Downloads', allowed: stats.isEgressAllowed },
    { label: 'Storage growth', allowed: stats.isStorageAllowed },
  ]
})

const anyBlocked = computed(() => entitlements.value.some((entitlement) => !entitlement.allowed))

// Each stage of the pipeline, so it is obvious where the savings come from.
const pipeline = computed(() => {
  const stats = usage.value
  if (!stats || stats.logicalBytes <= 0) return []
  return [
    { label: 'Logical (as uploaded)', bytes: stats.logicalBytes },
    { label: 'After deduplication', bytes: stats.storedBytes },
    { label: 'After compression', bytes: stats.compressedBytes },
  ].map((stage) => ({ ...stage, fraction: stage.bytes / stats.logicalBytes }))
})
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Usage" description="What this workspace stores, and what deduplication saves.">
      <template #badge>
        <Badge v-if="usage && !usage.isLimited" variant="secondary">No plan limit</Badge>
      </template>
    </PageHeader>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="!!usage"
      :skeleton-rows="4"
      @retry="refetch()"
    >
      <div v-if="usage" class="space-y-6">
        <Alert v-if="anyBlocked" variant="destructive">
          <AlertTitle>Some operations are blocked</AlertTitle>
          <AlertDescription>
            This workspace has reached a plan limit. Uploads or downloads will fail until usage
            drops or the plan changes.
          </AlertDescription>
        </Alert>

        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="On disk"
            :value="formatBytes(usage.compressedBytes)"
            hint="Billable footprint"
            :icon="HardDrive"
            numeric
          />
          <StatCard
            label="Reclaimed"
            :value="formatBytes(reclaimed)"
            :hint="overallReduction !== null ? `${formatPercent(overallReduction, 0)} smaller` : undefined"
            :icon="TrendingDown"
            numeric
          />
          <StatCard
            label="Repositories"
            :value="formatNumber(usage.repositoryCount)"
            :icon="Layers"
            numeric
          />
          <StatCard
            label="Releases"
            :value="formatNumber(usage.releaseCount)"
            :icon="Package"
            numeric
          />
        </div>

        <div class="grid gap-4 lg:grid-cols-3">
          <section class="bg-card hairline space-y-4 rounded-lg p-5 lg:col-span-2">
            <div>
              <h2 class="text-sm font-medium">Storage pipeline</h2>
              <p class="text-muted-foreground text-xs">
                Every release is chunked, deduplicated against what is already stored, then
                compressed.
              </p>
            </div>

            <div class="space-y-3">
              <div v-for="stage in pipeline" :key="stage.label" class="space-y-1.5">
                <div class="flex items-baseline justify-between gap-4 text-xs">
                  <span>{{ stage.label }}</span>
                  <span class="font-mono tabular-nums">{{ formatBytes(stage.bytes) }}</span>
                </div>
                <div class="bg-muted h-2 overflow-hidden rounded-full">
                  <div
                    class="bg-primary/70 h-full rounded-full transition-[width] duration-500"
                    :style="{ width: `${Math.max(stage.fraction * 100, 1)}%` }"
                  />
                </div>
              </div>
            </div>

            <dl class="border-hairline grid grid-cols-2 gap-3 border-t pt-4 text-xs">
              <div>
                <dt class="text-muted-foreground">Saved by deduplication</dt>
                <dd class="mt-0.5 font-mono tabular-nums">
                  {{ formatBytes(usage.deduplicationSavedBytes) }}
                </dd>
              </div>
              <div>
                <dt class="text-muted-foreground">Saved by compression</dt>
                <dd class="mt-0.5 font-mono tabular-nums">
                  {{ formatBytes(usage.compressionSavedBytes) }}
                </dd>
              </div>
            </dl>
          </section>

          <div class="space-y-4">
            <section class="bg-card hairline space-y-4 rounded-lg p-5">
              <h2 class="text-sm font-medium">Quota</h2>
              <UsageMeter
                :used="usage.compressedBytes"
                :limit="usage.maxStorageBytes"
                :is-limited="usage.isLimited"
              />
              <p v-if="!usage.isLimited" class="text-muted-foreground text-xs">
                This instance has no billing plugin configured, so storage is unmetered.
              </p>
            </section>

            <section class="bg-card hairline space-y-3 rounded-lg p-5">
              <h2 class="text-sm font-medium">Entitlements</h2>
              <ul class="space-y-2">
                <li
                  v-for="entitlement in entitlements"
                  :key="entitlement.label"
                  class="flex items-center justify-between gap-3 text-sm"
                >
                  <span>{{ entitlement.label }}</span>
                  <span
                    class="flex items-center gap-1.5 text-xs"
                    :class="entitlement.allowed ? 'text-success' : 'text-destructive'"
                  >
                    <component :is="entitlement.allowed ? CircleCheck : Ban" class="size-3.5" />
                    {{ entitlement.allowed ? 'Allowed' : 'Blocked' }}
                  </span>
                </li>
              </ul>
            </section>
          </div>
        </div>
      </div>
    </AsyncSection>
  </div>
</template>
