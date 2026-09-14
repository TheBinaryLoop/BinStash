<script setup lang="ts">
import { Hammer, RefreshCw, ShieldOff, Trash2 } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { toast } from 'vue-sonner'

import AsyncSection from '@/components/app/AsyncSection.vue'
import CollectGarbageDialog from '@/components/app/CollectGarbageDialog.vue'
import ConfirmDialog from '@/components/app/ConfirmDialog.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import JobProgress from '@/components/app/JobProgress.vue'
import PageBreadcrumbs from '@/components/app/PageBreadcrumbs.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  BackgroundJobsDocument,
  ChunkStoreDocument,
  ChunkStoreStatsDocument,
  CollectChunkStoreGarbageDocument,
  GcConfigDocument,
  RebuildChunkStoreDocument,
  UpgradeChunkStoreDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { jobTypeLabel } from '@/lib/jobs'
import { formatBytes, formatDate, formatNumber, formatPercent, formatRelative } from '@/lib/format'

const route = useRoute()
const chunkStoreId = computed(() => route.params.chunkStoreId as string)

const store = useQuery(ChunkStoreDocument, () => ({ id: chunkStoreId.value }))
const stats = useQuery(ChunkStoreStatsDocument, () => ({ chunkStoreId: chunkStoreId.value }))
const gcConfig = useQuery(GcConfigDocument, {})
/**
 * Polled while a job is in flight, and only then.
 *
 * The subscription carries live progress, but it cannot report what happened while nobody was
 * looking: a job that finished before this page was opened emits nothing, and a run whose stream
 * was cut by a server restart never emits again. Either leaves the cached status stuck on
 * Running with no event that will ever correct it.
 *
 * Held in a ref rather than derived inline, because the cadence depends on the query's own
 * result and the query reads this while it is being created.
 */
const jobPollInterval = ref(0)

const jobs = useQuery(
  BackgroundJobsDocument,
  () => ({ first: 10, chunkStoreId: chunkStoreId.value }),
  { pollInterval: jobPollInterval },
)

const detail = computed(() => store.result.value?.chunkStore)
const figures = computed(() => stats.result.value?.chunkStoreStats)
const jobList = computed(() => jobs.result.value?.backgroundJobs?.nodes ?? [])
const activeJob = computed(() =>
  jobList.value.find((job) => job.status === 'Running' || job.status === 'Pending'),
)

watch(activeJob, (active) => { jobPollInterval.value = active ? 5000 : 0 }, { immediate: true })

/**
 * Free space as a fraction, but only when the backend could report a volume at all — a
 * store that cannot measure its disk must not render as a full one.
 */
const volumeUsedFraction = computed(() => {
  const snapshot = figures.value
  if (!snapshot?.volumeTotalBytes) return null
  return (snapshot.volumeTotalBytes - snapshot.volumeFreeBytes) / snapshot.volumeTotalBytes
})

const volumeTone = computed(() => {
  const fraction = volumeUsedFraction.value
  if (fraction == null) return 'text-muted-foreground'
  if (fraction >= 0.95) return 'text-destructive'
  if (fraction >= 0.85) return 'text-warning'
  return 'text-muted-foreground'
})

const REFETCH = { refetchQueries: ['BackgroundJobs'] }
const { mutate: rebuild, loading: rebuilding } = useMutation(RebuildChunkStoreDocument, REFETCH)
const { mutate: upgrade, loading: upgrading } = useMutation(UpgradeChunkStoreDocument, REFETCH)
const { mutate: collect, loading: collecting } = useMutation(CollectChunkStoreGarbageDocument, REFETCH)

const rebuildOpen = ref(false)
const upgradeOpen = ref(false)
const collectOpen = ref(false)

async function confirmRebuild() {
  try {
    await rebuild({ chunkStoreId: chunkStoreId.value })
    rebuildOpen.value = false
    toast.success('Rebuild started.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not start the rebuild.'))
  }
}

async function confirmUpgrade() {
  try {
    await upgrade({ chunkStoreId: chunkStoreId.value })
    upgradeOpen.value = false
    toast.success('Release upgrade started.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not start the upgrade.'))
  }
}

async function confirmCollect({ dryRun, skipReclaim }: { dryRun: boolean; skipReclaim: boolean }) {
  try {
    await collect({
      chunkStoreId: chunkStoreId.value,
      dryRun,
      skipReclaim,
      retentionHours: null,
    })
    collectOpen.value = false
    toast.success(dryRun ? 'Collection report started.' : 'Collection started.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not start the collection.'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageBreadcrumbs
      :items="[
        { label: 'Chunk stores', to: { name: 'chunk-stores' } },
        { label: detail?.name ?? '…' },
      ]"
    />

    <AsyncSection
      :loading="store.loading.value"
      :error="store.error.value"
      :has-data="!!detail"
      :skeleton-rows="3"
      @retry="store.refetch()"
    >
      <div v-if="detail" class="space-y-6">
        <PageHeader :title="detail.name">
          <template #badge>
            <Badge variant="secondary" class="font-mono">{{ detail.type }}</Badge>
            <!-- Describes the health probe, NOT the store: a ReadOnly probe skips the write
                 round trip, so "healthy" means the path exists and has space rather than that
                 writes to it work. Deliberately not an alarm — ingest is never gated on this,
                 and badging it as "read-only" reads as "this store is refusing uploads". -->
            <Tooltip v-if="detail.probeMode === 'ReadOnly'">
              <TooltipTrigger as-child>
                <Badge variant="outline" class="cursor-default gap-1">
                  <ShieldOff class="size-3" />
                  write probe off
                </Badge>
              </TooltipTrigger>
              <TooltipContent class="max-w-xs">
                The health check reports free space but does not verify that writes succeed.
                Uploads are unaffected.
              </TooltipContent>
            </Tooltip>
          </template>
          <template #meta>
            <div class="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
              <span class="flex items-center gap-1.5">
                <span class="font-mono">{{ detail.id.slice(0, 8) }}</span>
                <CopyButton :value="detail.id" label="ID" />
              </span>
              <span v-if="detail.backendSettings?.localPath" class="font-mono">
                {{ detail.backendSettings.localPath }}
              </span>
              <span v-if="figures?.collectedAt">
                Measured {{ formatRelative(figures.collectedAt) }}
              </span>
            </div>
          </template>
          <template #actions>
            <Button
              variant="outline"
              class="gap-2"
              :disabled="collecting || !!activeJob"
              @click="collectOpen = true"
            >
              <Trash2 class="size-4" />
              Collect garbage
            </Button>
            <Button
              variant="outline"
              class="gap-2"
              :disabled="rebuilding || !!activeJob"
              @click="rebuildOpen = true"
            >
              <RefreshCw class="size-4" />
              Rebuild index
            </Button>
            <Button class="gap-2" :disabled="upgrading || !!activeJob" @click="upgradeOpen = true">
              <Hammer class="size-4" />
              Upgrade releases
            </Button>
          </template>
        </PageHeader>

        <p v-if="activeJob" class="text-muted-foreground text-xs">
          A job is already running for this store; further jobs are blocked until it finishes.
        </p>

        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Stored on disk"
            :value="formatBytes(figures?.physicalBytesTotal ?? 0)"
            :hint="
              figures?.totalLogicalBytes
                ? `${formatBytes(figures.totalLogicalBytes)} of releases`
                : undefined
            "
            numeric
          />
          <StatCard
            label="Space saved"
            :value="
              figures?.effectiveStorageRatio
                ? `${figures.effectiveStorageRatio.toFixed(1)}×`
                : '—'
            "
            hint="Logical bytes stored per byte on disk"
            numeric
          />
          <StatCard
            label="Chunks"
            :value="formatNumber(figures?.totalChunks ?? 0)"
            :hint="
              figures?.avgChunkSize ? `${formatBytes(figures.avgChunkSize)} average` : detail.chunker?.type
            "
            numeric
          />
          <StatCard
            label="Volume free"
            :value="figures?.volumeTotalBytes ? formatBytes(figures.volumeFreeBytes) : '—'"
            :hint="
              volumeUsedFraction != null
                ? `${formatPercent(volumeUsedFraction, 0)} of ${formatBytes(figures!.volumeTotalBytes)} used`
                : 'Not reported by this backend'
            "
            numeric
          />
        </div>

        <p v-if="volumeUsedFraction != null && volumeUsedFraction >= 0.85" :class="volumeTone" class="text-xs">
          This volume is {{ formatPercent(volumeUsedFraction, 0) }} full. Every workspace routed to
          this store stops accepting uploads when it fills.
        </p>

        <div class="grid gap-4 lg:grid-cols-2">
          <!-- Efficiency figures are instance-scoped by necessity: the store is shared, so these
               numbers describe every tenant's content at once and cannot be shown per workspace. -->
          <section class="bg-card hairline space-y-3 rounded-lg p-4">
            <h2 class="text-sm font-medium">Efficiency</h2>
            <dl class="space-y-2 text-xs">
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">Released data (logical)</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.totalLogicalBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">Unique chunks, uncompressed</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.uniqueLogicalChunkBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">Unique chunks, compressed</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.uniqueCompressedChunkBytes ?? 0) }}</dd>
              </div>
              <div class="border-hairline flex justify-between gap-3 border-t pt-2">
                <dt class="text-muted-foreground">Saved by deduplication</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.deduplicationSavedBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">Saved by compression</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.compressionSavedBytes ?? 0) }}</dd>
              </div>
            </dl>
          </section>

          <section class="bg-card hairline space-y-3 rounded-lg p-4">
            <h2 class="text-sm font-medium">On disk</h2>
            <dl class="space-y-2 text-xs">
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">
                  Chunk packs
                  <span v-if="figures?.chunkPackFileCount" class="opacity-70">
                    ({{ formatNumber(figures.chunkPackFileCount) }} files)
                  </span>
                </dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.chunkPackBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">
                  File definitions
                  <span v-if="figures?.fileDefinitionPackFileCount" class="opacity-70">
                    ({{ formatNumber(figures.fileDefinitionPackFileCount) }} files)
                  </span>
                </dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.fileDefinitionPackBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">
                  Release packages
                  <span v-if="figures?.releasePackageFileCount" class="opacity-70">
                    ({{ formatNumber(figures.releasePackageFileCount) }} files)
                  </span>
                </dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.releasePackageBytes ?? 0) }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-muted-foreground">
                  Indexes
                  <span v-if="figures?.indexFileCount" class="opacity-70">
                    ({{ formatNumber(figures.indexFileCount) }} files)
                  </span>
                </dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.indexBytes ?? 0) }}</dd>
              </div>
              <div class="border-hairline flex justify-between gap-3 border-t pt-2 font-medium">
                <dt>Total</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(figures?.physicalBytesTotal ?? 0) }}</dd>
              </div>
            </dl>
          </section>
        </div>

        <section class="space-y-3">
          <h2 class="text-sm font-medium">Jobs</h2>

          <p v-if="!jobList.length" class="text-muted-foreground text-sm">
            No maintenance jobs have run for this store.
          </p>

          <ul v-else class="space-y-2">
            <li
              v-for="job in jobList"
              :key="job.id"
              class="bg-card hairline space-y-3 rounded-lg p-4"
            >
              <div class="flex items-baseline justify-between gap-3">
                <p class="text-sm font-medium">{{ jobTypeLabel(job.jobType) }}</p>
                <p class="text-muted-foreground text-xs">{{ formatDate(job.createdAt) }}</p>
              </div>

              <JobProgress :job="job" />

              <p v-if="job.errorDetails" class="text-destructive font-mono text-xs">
                {{ job.errorDetails }}
              </p>
            </li>
          </ul>
        </section>
      </div>
    </AsyncSection>

    <CollectGarbageDialog
      v-model:open="collectOpen"
      :store-name="detail?.name ?? 'this store'"
      :retention-hours="gcConfig.result.value?.gcConfig?.retentionHours"
      :busy="collecting"
      @confirm="confirmCollect"
    />

    <ConfirmDialog
      v-model:open="rebuildOpen"
      title="Rebuild the chunk index?"
      confirm-label="Start rebuild"
      @confirm="confirmRebuild"
    >
      <p class="text-muted-foreground text-sm">
        Re-reads every pack file to reconstruct the index. Existing releases stay readable while it
        runs, but the store will be under sustained I/O.
      </p>
    </ConfirmDialog>

    <ConfirmDialog
      v-model:open="upgradeOpen"
      title="Upgrade releases to the current format?"
      confirm-label="Start upgrade"
      @confirm="confirmUpgrade"
    >
      <p class="text-muted-foreground text-sm">
        Rewrites release packages to the latest serializer version. This modifies stored data —
        take a backup first if this instance holds anything you cannot re-publish.
      </p>
    </ConfirmDialog>
  </div>
</template>
