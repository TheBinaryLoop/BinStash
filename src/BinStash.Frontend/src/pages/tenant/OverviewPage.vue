<script setup lang="ts">
import { ArrowRight, Boxes, FolderGit2, HardDrive, Package, Plus, Users } from '@lucide/vue'
import { computed } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import UsageMeter from '@/components/app/UsageMeter.vue'
import { Button } from '@/components/ui/button'
import { useQuery } from '@/composables/useGraphql'
import { RepositoriesDocument, TenantUsageDocument } from '@/graphql/generated'
import { formatBytes, formatNumber, formatRelative } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()

const usage = useQuery(TenantUsageDocument, {})
const repositories = useQuery(RepositoriesDocument, () => ({
  first: 5,
  order: [{ createdAt: 'DESC' as const }],
}))

const stats = computed(() => usage.result.value?.tenantUsage)
const repos = computed(() => repositories.result.value?.repositories?.nodes ?? [])
const repoTotal = computed(() => repositories.result.value?.repositories?.totalCount ?? 0)

const params = computed(() => ({ tenantId: tenants.activeTenantId }))

/** Savings are the product's whole value proposition, so lead with them. */
const reclaimed = computed(() =>
  stats.value
    ? stats.value.deduplicationSavedBytes + stats.value.compressionSavedBytes
    : 0,
)
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

    <AsyncSection
      :loading="usage.loading.value"
      :error="usage.error.value"
      :has-data="!!stats"
      :skeleton-rows="2"
      @retry="usage.refetch()"
    >
      <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
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
          label="On disk"
          :value="formatBytes(stats?.compressedBytes ?? 0)"
          hint="After dedup and compression"
          :icon="HardDrive"
          numeric
        />
        <StatCard
          label="Reclaimed"
          :value="formatBytes(reclaimed)"
          hint="Saved by dedup + compression"
          :icon="Boxes"
          numeric
        />
      </div>
    </AsyncSection>

    <div class="grid gap-4 lg:grid-cols-3">
      <section class="bg-card hairline space-y-4 rounded-lg p-4 lg:col-span-1">
        <div class="flex items-baseline justify-between">
          <h2 class="text-sm font-medium">Storage</h2>
          <RouterLink
            :to="{ name: 'usage', params }"
            class="text-muted-foreground hover:text-foreground text-xs"
          >
            Details
          </RouterLink>
        </div>

        <UsageMeter
          v-if="stats"
          :used="stats.compressedBytes"
          :limit="stats.maxStorageBytes"
          :is-limited="stats.isLimited"
        />

        <dl v-if="stats" class="space-y-1.5 text-xs">
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">Logical size</dt>
            <dd class="font-mono tabular-nums">{{ formatBytes(stats.logicalBytes) }}</dd>
          </div>
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">After dedup</dt>
            <dd class="font-mono tabular-nums">{{ formatBytes(stats.storedBytes) }}</dd>
          </div>
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">After compression</dt>
            <dd class="font-mono tabular-nums">{{ formatBytes(stats.compressedBytes) }}</dd>
          </div>
        </dl>
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
