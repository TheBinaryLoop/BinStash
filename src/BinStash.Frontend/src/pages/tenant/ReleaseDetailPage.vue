<script setup lang="ts">
import { ArrowLeft, Download, Package } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import AsyncSection from '@/components/app/AsyncSection.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useQuery } from '@/composables/useGraphql'
import { ReleaseDocument } from '@/graphql/generated'
import { formatBytes, formatDate, formatNumber, formatPercent } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const route = useRoute()
const tenants = useTenantStore()

const releaseId = computed(() => route.params.releaseId as string)
const repoId = computed(() => route.params.repoId as string)

const { result, loading, error, refetch } = useQuery(ReleaseDocument, () => ({
  id: releaseId.value,
}))

const release = computed(() => result.value?.release)
const metrics = computed(() => release.value?.metrics)

/**
 * The package download is binary and stays on REST — it is not a GraphQL-shaped
 * operation. The route is tenant-prefixed on the server.
 */
const downloadUrl = computed(
  () =>
    `/api/tenants/${tenants.activeTenantId}/repositories/${repoId.value}/releases/${releaseId.value}/download`,
)

const cliCommand = computed(() =>
  release.value?.repository
    ? `binstash release get --repo ${release.value.repository.name} --version ${release.value.version}`
    : '',
)

/** Custom properties are free-form JSON attached at publish time. */
const customProperties = computed<Array<[string, string]>>(() => {
  const raw = release.value?.customProperties
  if (!raw || typeof raw !== 'object') return []
  return Object.entries(raw as Record<string, unknown>).map(([key, value]) => [
    key,
    typeof value === 'object' ? JSON.stringify(value) : String(value ?? '—'),
  ])
})

const composition = computed(() => {
  const stats = metrics.value
  if (!stats) return []
  return [
    { label: 'Components', value: formatNumber(stats.componentsInRelease) },
    { label: 'Files', value: formatNumber(stats.filesInRelease) },
    { label: 'Chunks referenced', value: formatNumber(stats.chunksInRelease) },
    { label: 'Chunks new to the store', value: formatNumber(stats.newChunks) },
    { label: 'Metadata size', value: formatBytes(stats.metaBytesFull) },
  ]
})
</script>

<template>
  <div class="space-y-6">
    <Button
      variant="ghost"
      size="sm"
      class="text-muted-foreground -ml-2 gap-1.5"
      @click="$router.push({ name: 'repository', params: { tenantId: tenants.activeTenantId, repoId } })"
    >
      <ArrowLeft class="size-3.5" />
      {{ release?.repository?.name ?? 'Repository' }}
    </Button>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="!!release"
      :skeleton-rows="4"
      @retry="refetch()"
    >
      <div v-if="release" class="space-y-6">
        <PageHeader :title="release.version">
          <template #badge>
            <Badge variant="secondary" class="font-mono">
              {{ release.repository?.storageClass }}
            </Badge>
          </template>
          <template #meta>
            <div class="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
              <span>Published {{ formatDate(release.createdAt) }}</span>
              <span class="flex items-center gap-1.5">
                <span class="font-mono">{{ release.id.slice(0, 8) }}</span>
                <CopyButton :value="release.id" label="ID" />
              </span>
            </div>
          </template>
          <template #actions>
            <Button as="a" :href="downloadUrl" download class="gap-2">
              <Download class="size-4" />
              Download
            </Button>
          </template>
        </PageHeader>

        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Logical size"
            :value="formatBytes(metrics?.totalLogicalBytes ?? null)"
            hint="As published"
            numeric
          />
          <StatCard
            label="Added to store"
            :value="formatBytes(metrics?.newCompressedBytes ?? null)"
            hint="Compressed, after dedup"
            numeric
          />
          <StatCard
            label="Deduplicated"
            :value="formatBytes(metrics?.deduplicationSavedBytes ?? null)"
            :hint="
              metrics ? `${formatPercent(metrics.incrementalDeduplicationRatio, 0)} of content` : undefined
            "
            numeric
          />
          <StatCard
            label="New data"
            :value="metrics ? formatPercent(metrics.newDataPercent / 100, 1) : '—'"
            hint="Share not already stored"
            numeric
          />
        </div>

        <div class="grid gap-4 lg:grid-cols-3">
          <section class="bg-card hairline space-y-4 rounded-lg p-5 lg:col-span-2">
            <h2 class="text-sm font-medium">Composition</h2>
            <dl class="grid gap-x-8 gap-y-2.5 sm:grid-cols-2">
              <div
                v-for="item in composition"
                :key="item.label"
                class="flex justify-between gap-4 text-sm"
              >
                <dt class="text-muted-foreground">{{ item.label }}</dt>
                <dd class="font-mono tabular-nums">{{ item.value }}</dd>
              </div>
            </dl>

            <div v-if="metrics" class="border-hairline space-y-2.5 border-t pt-4">
              <h3 class="text-sm font-medium">Effectiveness</h3>
              <div
                v-for="ratio in [
                  { label: 'Deduplication', value: metrics.incrementalDeduplicationRatio },
                  { label: 'Compression', value: metrics.incrementalCompressionRatio },
                  { label: 'Combined', value: metrics.incrementalEffectiveRatio },
                ]"
                :key="ratio.label"
                class="space-y-1"
              >
                <div class="flex items-baseline justify-between gap-4 text-xs">
                  <span>{{ ratio.label }}</span>
                  <span class="font-mono tabular-nums">
                    {{ formatPercent(Math.max(0, 1 - ratio.value), 0) }} smaller
                  </span>
                </div>
                <div class="bg-muted h-1.5 overflow-hidden rounded-full">
                  <div
                    class="bg-success/70 h-full rounded-full"
                    :style="{ width: `${Math.min(Math.max(1 - ratio.value, 0), 1) * 100}%` }"
                  />
                </div>
              </div>
            </div>
          </section>

          <div class="space-y-4">
            <section v-if="release.notes" class="bg-card hairline space-y-2 rounded-lg p-5">
              <h2 class="text-sm font-medium">Release notes</h2>
              <p class="text-muted-foreground text-sm whitespace-pre-wrap">{{ release.notes }}</p>
            </section>

            <section class="bg-card hairline space-y-3 rounded-lg p-5">
              <h2 class="text-sm font-medium">Custom properties</h2>
              <dl v-if="customProperties.length" class="space-y-2">
                <div
                  v-for="[key, value] in customProperties"
                  :key="key"
                  class="flex justify-between gap-4 text-xs"
                >
                  <dt class="text-muted-foreground truncate">{{ key }}</dt>
                  <dd class="truncate font-mono">{{ value }}</dd>
                </div>
              </dl>
              <p v-else class="text-muted-foreground text-sm">None.</p>
            </section>

            <section class="bg-card hairline space-y-2 rounded-lg p-5">
              <div class="flex items-center justify-between gap-2">
                <h2 class="text-sm font-medium">Fetch from the CLI</h2>
                <CopyButton :value="cliCommand" />
              </div>
              <code class="bg-muted text-muted-foreground block overflow-x-auto rounded p-2 font-mono text-xs">
                {{ cliCommand }}
              </code>
            </section>
          </div>
        </div>
      </div>

      <div v-else class="text-muted-foreground flex flex-col items-center gap-3 py-16">
        <Package class="size-6" />
        <p class="text-sm">Release not found.</p>
      </div>
    </AsyncSection>
  </div>
</template>
