<script setup lang="ts">
import { ArrowRight, Ban, CircleCheck, FolderGit2, HardDrive, Package, Plus } from '@lucide/vue'
import { computed } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import QuotaAlert from '@/components/app/QuotaAlert.vue'
import TrafficChart from '@/components/app/TrafficChart.vue'
import StatCard from '@/components/app/StatCard.vue'
import UsageMeter from '@/components/app/UsageMeter.vue'
import { Button } from '@/components/ui/button'
import { useQuery } from '@/composables/useGraphql'
import { RepositoriesDocument, TenantTrafficDocument, TenantUsageDocument } from '@/graphql/generated'
import { formatBytes, formatNumber, formatRelative } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()

const usage = useQuery(TenantUsageDocument, {})
const traffic = useQuery(TenantTrafficDocument, { grain: 'HOURLY' as const })
const repositories = useQuery(RepositoriesDocument, () => ({
  first: 5,
  order: [{ createdAt: 'DESC' as const }],
}))

const stats = computed(() => usage.result.value?.tenantUsage)
const trafficSeries = computed(() => traffic.result.value?.tenantTraffic)
const repos = computed(() => repositories.result.value?.repositories?.nodes ?? [])
const repoTotal = computed(() => repositories.result.value?.repositories?.totalCount ?? 0)

const params = computed(() => ({ tenantId: tenants.activeTenantId }))

/**
 * The three limits the server actually enforces — an ingest session is refused when uploads are
 * off, a finalize when storage is, a download when downloads are. Shown here rather than only on
 * the usage page because this is where someone looks first when a build has started failing.
 */
const entitlements = computed(() => {
  const s = stats.value
  if (!s) return []
  return [
    { label: 'Uploads', allowed: s.isIngestAllowed },
    { label: 'Downloads', allowed: s.isEgressAllowed },
    { label: 'Storage growth', allowed: s.isStorageAllowed },
  ]
})

/**
 * The billed figure is the workspace's own content deduplicated against itself, so the gap
 * between it and the released total is a saving this workspace actually receives — worth
 * stating on the card rather than leaving the smaller number unexplained.
 */
const savedHint = computed(() => {
  const s = stats.value
  if (!s || s.deduplicationSavedBytes <= 0) return 'Deduplicated across your releases'
  return `${formatBytes(s.deduplicationSavedBytes)} not charged`
})

</script>

<template>
  <div class="space-y-6">
    <PageHeader
      :title="tenants.activeTenant?.name ?? 'Overview'"
      description="Storage, repositories and recent activity in this workspace."
    >
      <template #actions>
        <Button class="gap-2" @click="$router.push({ name: 'repositories', params })">
          <Plus class="size-4" />
          New repository
        </Button>
      </template>
    </PageHeader>

    <QuotaAlert :usage="stats" :details-to="{ name: 'usage', params }" />

    <AsyncSection
      :loading="usage.loading.value"
      :error="usage.error.value"
      :has-data="!!stats"
      :skeleton-rows="2"
      @retry="usage.refetch()"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <StatCard
          label="Repositories"
          :value="formatNumber(stats?.repositoryCount ?? 0)"
          :icon="FolderGit2"
          numeric
        />
        <StatCard
          label="Releases"
          :value="formatNumber(stats?.releaseCount ?? 0)"
          :icon="Package"
          numeric
        />
        <StatCard
          label="Billed storage"
          :value="formatBytes(stats?.uniqueLogicalBytes ?? 0)"
          :hint="savedHint"
          :icon="HardDrive"
          numeric
        />
      </div>
    </AsyncSection>

    <section class="bg-card hairline space-y-4 rounded-lg p-4">
      <div>
        <h2 class="text-sm font-medium">Traffic</h2>
        <p class="text-muted-foreground text-xs">
          Bytes uploaded and downloaded over the last 7 days. Measured on the wire, so it reads
          smaller than what the releases logically weigh.
        </p>
      </div>

      <AsyncSection
        :loading="traffic.loading.value"
        :error="traffic.error.value"
        :has-data="!!trafficSeries"
        :skeleton-rows="3"
        @retry="traffic.refetch()"
      >
        <TrafficChart
          v-if="trafficSeries"
          :points="trafficSeries.points"
          :from-utc="trafficSeries.fromUtc"
          :to-utc="trafficSeries.toUtc"
          grain="HOURLY"
        />
      </AsyncSection>
    </section>

    <div class="grid gap-4 lg:grid-cols-3">
      <section class="bg-card hairline space-y-4 self-start rounded-lg p-4 lg:col-span-1">
        <div class="flex items-baseline justify-between">
          <h2 class="text-sm font-medium">Storage &amp; quota</h2>
          <RouterLink
            :to="{ name: 'usage', params }"
            class="text-muted-foreground hover:text-foreground text-xs"
          >
            Details
          </RouterLink>
        </div>

        <UsageMeter
          v-if="stats"
          :used="stats.uniqueLogicalBytes"
          :limit="stats.maxStorageBytes"
          :is-limited="stats.isLimited"
        />

        <dl v-if="stats" class="space-y-1.5 text-xs">
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">Released content</dt>
            <dd class="font-mono tabular-nums">{{ formatBytes(stats.logicalBytes) }}</dd>
          </div>
          <div v-if="stats.deduplicationSavedBytes > 0" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">Not charged</dt>
            <dd class="text-success font-mono tabular-nums">
              −{{ formatBytes(stats.deduplicationSavedBytes) }}
            </dd>
          </div>
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">Repositories</dt>
            <dd class="font-mono tabular-nums">{{ formatNumber(stats.repositoryCount) }}</dd>
          </div>
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">Releases</dt>
            <dd class="font-mono tabular-nums">{{ formatNumber(stats.releaseCount) }}</dd>
          </div>
        </dl>

        <ul v-if="stats" class="border-hairline space-y-1.5 border-t pt-3 text-xs">
          <li
            v-for="entitlement in entitlements"
            :key="entitlement.label"
            class="flex items-center justify-between gap-3"
          >
            <span class="text-muted-foreground">{{ entitlement.label }}</span>
            <span
              class="flex items-center gap-1.5"
              :class="entitlement.allowed ? 'text-success' : 'text-destructive'"
            >
              <component :is="entitlement.allowed ? CircleCheck : Ban" class="size-3.5" />
              {{ entitlement.allowed ? 'Allowed' : 'Blocked' }}
            </span>
          </li>
        </ul>
      </section>

      <section class="space-y-3 lg:col-span-2">
        <div class="flex items-baseline justify-between">
          <h2 class="text-sm font-medium">Recent repositories</h2>
          <RouterLink
            v-if="repoTotal"
            :to="{ name: 'repositories', params }"
            class="text-muted-foreground hover:text-foreground text-xs"
          >
            View all {{ repoTotal }}
          </RouterLink>
        </div>

        <AsyncSection
          :loading="repositories.loading.value"
          :error="repositories.error.value"
          :has-data="repos.length > 0"
          :skeleton-rows="3"
          @retry="repositories.refetch()"
        >
          <EmptyState
            v-if="!repos.length"
            :icon="FolderGit2"
            title="No repositories yet"
            description="A repository groups the releases of one artifact."
          >
            <Button size="sm" class="gap-2" @click="$router.push({ name: 'repositories', params })">
              <Plus class="size-4" />
              Create a repository
            </Button>
          </EmptyState>

          <ul v-else class="space-y-2">
            <li v-for="repo in repos" :key="repo.id">
              <RouterLink
                :to="{ name: 'repository', params: { ...params, repoId: repo.id } }"
                class="bg-card hairline hover:border-primary/40 group flex items-center gap-3 rounded-lg px-3 py-2.5 transition-colors"
              >
                <FolderGit2 class="text-muted-foreground size-4 shrink-0" />
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-sm font-medium">{{ repo.name }}</span>
                  <span class="text-muted-foreground block truncate text-xs">
                    <template v-if="repo.releases?.nodes?.[0]">
                      {{ repo.releases.totalCount }} release{{
                        repo.releases.totalCount === 1 ? '' : 's'
                      }}
                      · latest
                      <span class="font-mono">{{ repo.releases.nodes[0].version }}</span>
                      {{ formatRelative(repo.releases.nodes[0].createdAt) }}
                    </template>
                    <template v-else>No releases yet</template>
                  </span>
                </span>
                <span class="text-muted-foreground shrink-0 font-mono text-xs">
                  {{ repo.storageClass }}
                </span>
                <ArrowRight
                  class="text-muted-foreground group-hover:text-foreground size-4 shrink-0 transition-colors"
                />
              </RouterLink>
            </li>
          </ul>
        </AsyncSection>
      </section>
    </div>
  </div>
</template>
