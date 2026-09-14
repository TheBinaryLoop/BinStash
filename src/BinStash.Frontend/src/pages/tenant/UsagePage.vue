<script setup lang="ts">
import { Ban, CircleCheck, FolderGit2, HardDrive, Info, Layers, Package } from '@lucide/vue'
import { computed } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import QuotaAlert from '@/components/app/QuotaAlert.vue'
import StatCard from '@/components/app/StatCard.vue'
import UsageMeter from '@/components/app/UsageMeter.vue'
import { Badge } from '@/components/ui/badge'
import { useQuery } from '@/composables/useGraphql'
import { TenantUsageDocument } from '@/graphql/generated'
import { formatBytes, formatNumber, formatRelative } from '@/lib/format'

/**
 * The billed figure is `uniqueLogicalBytes`: this workspace's content deduplicated against
 * ITSELF, as if it had a private chunk store. Deduplicating within the workspace is safe to
 * show precisely because it consults no other workspace's data.
 *
 * What is still NOT shown is the footprint after deduplication ACROSS workspaces, and any
 * compression ratio of the shared store. Those move when other tenants upload, so displaying
 * them would leak their content. They belong on instance-admin surfaces.
 */
const { result, loading, error, refetch } = useQuery(TenantUsageDocument, {})
const usage = computed(() => result.value?.tenantUsage)

const entitlements = computed(() => {
  const stats = usage.value
  if (!stats) return []
  return [
    { label: 'Uploads', allowed: stats.isIngestAllowed },
    { label: 'Downloads', allowed: stats.isEgressAllowed },
    { label: 'Storage growth', allowed: stats.isStorageAllowed },
  ]
})

const averageReleaseSize = computed(() => {
  const stats = usage.value
  if (!stats || stats.releaseCount === 0) return null
  return stats.logicalBytes / stats.releaseCount
})

/**
 * How much of the released content deduplication keeps off the bill. Stated as a share as well
 * as a size because the share is what makes the model legible: a workspace publishing one
 * release per platform sees most of it.
 */
const savedLabel = computed(() => {
  const stats = usage.value
  if (!stats || stats.logicalBytes <= 0 || stats.deduplicationSavedBytes <= 0) {
    return 'Total size of all releases'
  }
  const share = Math.round((stats.deduplicationSavedBytes / stats.logicalBytes) * 100)
  return `${formatBytes(stats.deduplicationSavedBytes)} (${share}%) not charged`
})

/**
 * The footprint is established by a periodic walk, so a workspace that just uploaded should be
 * able to see that the figure predates their upload rather than conclude the upload was free.
 */
const footprintAge = computed(() => {
  const at = usage.value?.footprintComputedAt
  return at ? formatRelative(at) : null
})
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Usage" description="What this workspace stores, and how it counts against your plan.">
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
        <QuotaAlert :usage="usage" />

        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Billed storage"
            :value="formatBytes(usage.uniqueLogicalBytes)"
            :hint="footprintAge ? `Measured ${footprintAge}` : 'Not yet measured'"
            :icon="HardDrive"
            numeric
          />
          <StatCard
            label="Released content"
            :value="formatBytes(usage.logicalBytes)"
            :hint="savedLabel"
            :icon="Layers"
            numeric
          />
          <StatCard
            label="Releases"
            :value="formatNumber(usage.releaseCount)"
            :hint="averageReleaseSize !== null ? `${formatBytes(averageReleaseSize)} average` : undefined"
            :icon="Package"
            numeric
          />
          <StatCard
            label="Repositories"
            :value="formatNumber(usage.repositoryCount)"
            :icon="FolderGit2"
            numeric
          />
        </div>

        <div class="grid gap-4 lg:grid-cols-3">
          <section class="bg-card hairline space-y-4 rounded-lg p-5 lg:col-span-2">
            <div>
              <h2 class="text-sm font-medium">Plan usage</h2>
              <p class="text-muted-foreground text-xs">
                Measured on your content after deduplicating it against itself.
              </p>
            </div>

            <UsageMeter
              :used="usage.uniqueLogicalBytes"
              :limit="usage.maxStorageBytes"
              :is-limited="usage.isLimited"
            />

            <div
              class="border-hairline text-muted-foreground flex gap-2.5 border-t pt-4 text-xs"
            >
              <Info class="mt-px size-3.5 shrink-0" />
              <p>
                You are billed for the distinct content this workspace holds, counted once. Bytes
                repeated between your releases — successive versions, or the platforms of one
                multi-target release — are charged once, not once per copy. The figure depends
                only on your own data, so it never moves because of what another workspace
                uploads. BinStash compresses and deduplicates further across workspaces before
                writing to disk; that saving is the operator's and is not reflected here.
              </p>
            </div>
          </section>

          <section class="bg-card hairline h-fit space-y-3 rounded-lg p-5">
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
            <p v-if="!usage.isLimited" class="text-muted-foreground border-hairline border-t pt-3 text-xs">
              This instance has no billing plugin configured, so storage is unmetered.
            </p>
          </section>
        </div>
      </div>
    </AsyncSection>
  </div>
</template>
