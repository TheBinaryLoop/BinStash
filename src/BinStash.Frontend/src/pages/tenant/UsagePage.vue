<script setup lang="ts">
import { Ban, CircleCheck, HardDrive, Info, Layers, Package } from '@lucide/vue'
import { computed } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import UsageMeter from '@/components/app/UsageMeter.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { useQuery } from '@/composables/useGraphql'
import { TenantUsageDocument } from '@/graphql/generated'
import { formatBytes, formatNumber } from '@/lib/format'

/**
 * Everything here is expressed in undeduplicated, uncompressed logical bytes — what the
 * workspace is billed on. Deduplicated footprint and the savings it produces are NOT
 * shown: the chunk store is shared between workspaces, so those numbers depend on other
 * tenants' content and would leak it. They belong on instance-admin surfaces.
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

const anyBlocked = computed(() => entitlements.value.some((entitlement) => !entitlement.allowed))

const averageReleaseSize = computed(() => {
  const stats = usage.value
  if (!stats || stats.releaseCount === 0) return null
  return stats.logicalBytes / stats.releaseCount
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
        <Alert v-if="anyBlocked" variant="destructive">
          <AlertTitle>Some operations are blocked</AlertTitle>
          <AlertDescription>
            This workspace has reached a plan limit. Uploads or downloads will fail until usage
            drops or the plan changes.
          </AlertDescription>
        </Alert>

        <div class="grid gap-3 sm:grid-cols-3">
          <StatCard
            label="Stored"
            :value="formatBytes(usage.logicalBytes)"
            hint="Billable — total size of all releases"
            :icon="HardDrive"
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
            :hint="averageReleaseSize !== null ? `${formatBytes(averageReleaseSize)} average` : undefined"
            :icon="Package"
            numeric
          />
        </div>

        <div class="grid gap-4 lg:grid-cols-3">
          <section class="bg-card hairline space-y-4 rounded-lg p-5 lg:col-span-2">
            <div>
              <h2 class="text-sm font-medium">Plan usage</h2>
              <p class="text-muted-foreground text-xs">
                Measured on the total size of your releases as published.
              </p>
            </div>

            <UsageMeter
              :used="usage.logicalBytes"
              :limit="usage.maxStorageBytes"
              :is-limited="usage.isLimited"
            />

            <div
              class="border-hairline text-muted-foreground flex gap-2.5 border-t pt-4 text-xs"
            >
              <Info class="mt-px size-3.5 shrink-0" />
              <p>
                BinStash deduplicates and compresses your data before writing it to disk, so the
                space it physically occupies is smaller than the figure above. Because that
                storage is shared across workspaces, billing is based on your releases'
                uncompressed, undeduplicated size rather than on the shared footprint.
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
