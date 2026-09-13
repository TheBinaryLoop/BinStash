<script setup lang="ts">
import { ArrowLeft, Hammer, RefreshCw } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { toast } from 'vue-sonner'

import AsyncSection from '@/components/app/AsyncSection.vue'
import ConfirmDialog from '@/components/app/ConfirmDialog.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import JobProgress from '@/components/app/JobProgress.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import StatCard from '@/components/app/StatCard.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  BackgroundJobsDocument,
  ChunkStoreDocument,
  ChunkStoreStatsDocument,
  RebuildChunkStoreDocument,
  UpgradeChunkStoreDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatBytes, formatDate, formatNumber } from '@/lib/format'

const route = useRoute()
const chunkStoreId = computed(() => route.params.chunkStoreId as string)

const store = useQuery(ChunkStoreDocument, () => ({ id: chunkStoreId.value }))
const stats = useQuery(ChunkStoreStatsDocument, () => ({ chunkStoreId: chunkStoreId.value }))
const jobs = useQuery(BackgroundJobsDocument, () => ({
  first: 10,
  chunkStoreId: chunkStoreId.value,
}))

const detail = computed(() => store.result.value?.chunkStore)
const jobList = computed(() => jobs.result.value?.backgroundJobs?.nodes ?? [])
const activeJob = computed(() =>
  jobList.value.find((job) => job.status === 'Running' || job.status === 'Pending'),
)

const REFETCH = { refetchQueries: ['BackgroundJobs'] }
const { mutate: rebuild, loading: rebuilding } = useMutation(RebuildChunkStoreDocument, REFETCH)
const { mutate: upgrade, loading: upgrading } = useMutation(UpgradeChunkStoreDocument, REFETCH)

const rebuildOpen = ref(false)
const upgradeOpen = ref(false)

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
</script>

<template>
  <div class="space-y-6">
    <Button
      variant="ghost"
      size="sm"
      class="text-muted-foreground -ml-2 gap-1.5"
      @click="$router.push({ name: 'chunk-stores' })"
    >
      <ArrowLeft class="size-3.5" />
      Chunk stores
    </Button>

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
            </div>
          </template>
          <template #actions>
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

        <div class="grid gap-3 sm:grid-cols-3">
          <StatCard
            label="Chunks stored"
            :value="formatNumber(stats.result.value?.chunkStoreStats?.totalChunks ?? 0)"
            numeric
          />
          <StatCard
            label="Average chunk"
            :value="formatBytes(detail.chunker?.avgChunkSize ?? null)"
            :hint="detail.chunker?.type"
            numeric
          />
          <StatCard
            label="Chunk range"
            :value="`${formatBytes(detail.chunker?.minChunkSize ?? null)} – ${formatBytes(detail.chunker?.maxChunkSize ?? null)}`"
            numeric
          />
        </div>

        <section class="space-y-3">
          <h2 class="text-sm font-medium">Jobs</h2>

          <p v-if="!jobList.length" class="text-muted-foreground text-sm">
            No rebuild or upgrade jobs have run for this store.
          </p>

          <ul v-else class="space-y-2">
            <li
              v-for="job in jobList"
              :key="job.id"
              class="bg-card hairline space-y-3 rounded-lg p-4"
            >
              <div class="flex items-baseline justify-between gap-3">
                <p class="text-sm font-medium">{{ job.jobType }}</p>
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
