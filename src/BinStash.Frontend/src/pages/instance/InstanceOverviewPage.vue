<script setup lang="ts">
import { Activity, Building2, Database, FolderGit2, HardDrive, Package, Users } from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useQuery } from '@/composables/useGraphql'
import { BackgroundJobsDocument, ChunkStoresDocument, InstanceStatsDocument } from '@/graphql/generated'
import { apiJson } from '@/lib/http'
import { formatBytes, formatNumber, formatRelative } from '@/lib/format'
import { jobTypeLabel } from '@/lib/jobs'

const stats = useQuery(InstanceStatsDocument, {})
const chunkStores = useQuery(ChunkStoresDocument, { first: 100 })
// Same reasoning as the chunk store page: poll only while something is running, so a job that
// ended unobserved stops being reported as still going. The ref breaks the cycle between the
// query and the result it polls on.
const jobPollInterval = ref(0)
const jobs = useQuery(BackgroundJobsDocument, { first: 5 }, { pollInterval: jobPollInterval })

const instanceStats = computed(() => stats.result.value?.instanceStats)
const storeCount = computed(
  () => instanceStats.value?.chunkStoreCount ?? chunkStores.result.value?.chunkStores?.totalCount ?? 0,
)
const recentJobs = computed(() => jobs.result.value?.backgroundJobs?.nodes ?? [])

watch(
  () => recentJobs.value.some((job) => job.status === 'Running' || job.status === 'Pending'),
  (busy) => { jobPollInterval.value = busy ? 5000 : 0 },
  { immediate: true },
)

/**
 * How much of the instance's logical data each physical byte carries. Instance-wide, so unlike
 * the per-workspace views this can be shown without leaking one tenant's content to another.
 */
const storageEfficiency = computed(() => {
  const figures = instanceStats.value
  if (!figures?.totalPhysicalBytes) return null
  return figures.totalLogicalBytes / figures.totalPhysicalBytes
})

/** Health is a plain REST probe, not part of the graph. */
const health = ref<'checking' | 'healthy' | 'degraded' | 'unreachable'>('checking')

onMounted(async () => {
  try {
    const report = await apiJson<{ status?: string }>('/health')
    health.value = report.status?.toLowerCase() === 'healthy' ? 'healthy' : 'degraded'
  } catch {
    health.value = 'unreachable'
  }
})

const healthTone = computed(() =>
  ({
    checking: 'secondary',
    healthy: 'secondary',
    degraded: 'destructive',
    unreachable: 'destructive',
  })[health.value] as 'secondary' | 'destructive',
)

const statusTone: Record<string, string> = {
  Completed: 'text-success',
  Running: 'text-primary',
  Pending: 'text-muted-foreground',
  Failed: 'text-destructive',
  Cancelled: 'text-muted-foreground',
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Instance" description="Everything hosted on this BinStash deployment.">
      <template #badge>
        <Badge :variant="healthTone" class="gap-1.5">
          <span
            class="size-1.5 rounded-full"
            :class="health === 'healthy' ? 'bg-success' : 'bg-current'"
          />
          {{ health === 'checking' ? 'Checking…' : health }}
        </Badge>
      </template>
    </PageHeader>

    <AsyncSection
      :loading="stats.loading.value"
      :error="stats.error.value"
      :has-data="!!instanceStats"
      :skeleton-rows="2"
      @retry="stats.refetch()"
    >
      <div class="space-y-3">
        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Workspaces"
            :value="formatNumber(instanceStats?.tenantCount ?? 0)"
            :icon="Building2"
            numeric
          />
          <StatCard
            label="Users"
            :value="formatNumber(instanceStats?.userCount ?? 0)"
            :icon="Users"
            numeric
          />
          <StatCard
            label="Repositories"
            :value="formatNumber(instanceStats?.repositoryCount ?? 0)"
            :hint="
              instanceStats?.releaseCount
                ? `${formatNumber(instanceStats.releaseCount)} releases`
                : undefined
            "
            :icon="FolderGit2"
            numeric
          />
          <StatCard
            label="Chunk stores"
            :value="formatNumber(storeCount)"
            :icon="Database"
            numeric
          />
        </div>

        <!-- What the deployment actually costs in disk, and how much headroom is left. Both
             were already collected hourly; neither had anywhere to appear. -->
        <div class="grid gap-3 sm:grid-cols-3">
          <StatCard
            label="Stored on disk"
            :value="formatBytes(instanceStats?.totalPhysicalBytes ?? 0)"
            :hint="
              instanceStats?.totalLogicalBytes
                ? `${formatBytes(instanceStats.totalLogicalBytes)} of releases`
                : undefined
            "
            :icon="HardDrive"
            numeric
          />
          <StatCard
            label="Space saved"
            :value="storageEfficiency ? `${storageEfficiency.toFixed(1)}×` : '—'"
            hint="Logical bytes per byte on disk"
            :icon="Package"
            numeric
          />
          <StatCard
            label="Tightest volume"
            :value="formatBytes(instanceStats?.minVolumeFreeBytes ?? null)"
            :hint="
              instanceStats?.minVolumeFreeChunkStoreName
                ? `free on ${instanceStats.minVolumeFreeChunkStoreName}`
                : 'No volume reported'
            "
            :icon="Database"
            numeric
          />
        </div>
      </div>
    </AsyncSection>

    <div class="grid gap-4 lg:grid-cols-2">
      <section class="space-y-3">
        <div class="flex items-baseline justify-between">
          <h2 class="text-sm font-medium">Recent background jobs</h2>
          <RouterLink
            :to="{ name: 'chunk-stores' }"
            class="text-muted-foreground hover:text-foreground text-xs"
          >
            Chunk stores
          </RouterLink>
        </div>

        <ul v-if="recentJobs.length" class="space-y-2">
          <li
            v-for="job in recentJobs"
            :key="job.id"
            class="bg-card hairline flex items-center gap-3 rounded-lg px-3 py-2.5"
          >
            <Activity class="text-muted-foreground size-4 shrink-0" />
            <div class="min-w-0 flex-1">
              <p class="truncate text-sm">{{ jobTypeLabel(job.jobType) }}</p>
              <p class="text-muted-foreground text-xs">
                {{ formatRelative(job.createdAt) }}
              </p>
            </div>
            <span class="shrink-0 text-xs" :class="statusTone[job.status] ?? ''">
              {{ job.status }}
            </span>
          </li>
        </ul>
        <p v-else class="text-muted-foreground text-sm">No background jobs have run yet.</p>
      </section>

      <section class="space-y-3">
        <h2 class="text-sm font-medium">Quick actions</h2>
        <div class="grid gap-2 sm:grid-cols-2">
          <Button
            v-for="action in [
              { label: 'Manage tenants', to: 'instance-tenants' },
              { label: 'Manage users', to: 'instance-users' },
              { label: 'Chunk stores', to: 'chunk-stores' },
              { label: 'Instance settings', to: 'instance-settings' },
            ]"
            :key="action.to"
            variant="outline"
            class="justify-start"
            @click="$router.push({ name: action.to })"
          >
            {{ action.label }}
          </Button>
        </div>
      </section>
    </div>
  </div>
</template>
