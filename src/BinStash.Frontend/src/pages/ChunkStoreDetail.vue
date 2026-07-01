<template>
  <div class="flex h-dvh overflow-hidden">
    <Sidebar :sidebarOpen="sidebarOpen" @close-sidebar="sidebarOpen = false" />

    <div class="relative flex flex-col flex-1 overflow-y-auto overflow-x-hidden">
      <Header :sidebarOpen="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" />

      <main class="grow">
        <div class="px-4 sm:px-6 lg:px-8 py-8 w-full max-w-384 mx-auto space-y-6">

          <!-- Breadcrumb -->
          <Breadcrumbs
            :items="[
              { label: 'Chunk Stores', to: '/chunk-stores' },
              { label: chunkStore?.name ?? '…' },
            ]"
          />

          <!-- Loading skeleton -->
          <div v-if="loading" class="space-y-6">
            <BaseCard accent>
              <div class="space-y-4">
                <Skeleton width="8rem" height="0.75rem" />
                <Skeleton width="16rem" height="2.5rem" />
                <Skeleton width="100%" height="4rem" rounded="card" />
              </div>
            </BaseCard>
          </div>

          <!-- Error state -->
          <div v-else-if="loadError" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
            {{ loadError }}
          </div>

          <!-- Main content -->
          <template v-else-if="chunkStore">

            <!-- Header card -->
            <BaseCard accent>
              <div class="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                <div class="flex items-start gap-3 min-w-0">
                  <div class="flex size-11 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
                    <IconDatabase class="size-6" />
                  </div>
                  <div class="min-w-0">
                    <h1 class="truncate text-2xl font-bold text-ink-strong">{{ chunkStore.name }}</h1>
                    <p class="mt-1.5 text-sm text-ink-muted">
                      {{ chunkStore.type }} chunk store
                      <span v-if="chunkStore.backendSettings?.localPath"> &middot; {{ chunkStore.backendSettings.localPath }}</span>
                    </p>
                  </div>
                </div>
                <div class="flex items-center gap-2">
                  <BaseButton
                    :icon="IconArrowUp"
                    :loading="upgradeStarting"
                    :disabled="isUpgrading || upgradeStarting"
                    @click="startUpgrade"
                  >
                    <span v-if="isUpgrading">Upgrade running…</span>
                    <span v-else>Upgrade Releases</span>
                  </BaseButton>
                </div>
              </div>
            </BaseCard>

            <!-- Upgrade error -->
            <div v-if="upgradeError" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
              {{ upgradeError }}
            </div>

            <!-- Real-time upgrade progress -->
            <BaseCard v-if="activeProgress || activeJob" :padded="false">
              <template #header>
                <div class="flex items-center gap-2">
                  <div class="flex size-8 items-center justify-center rounded-full bg-accent-soft text-accent">
                    <IconArrowUp class="h-4 w-4" />
                  </div>
                  <h2 class="font-semibold text-ink-strong">Upgrade Progress</h2>
                </div>
              </template>
              <template #actions>
                <div class="flex items-center gap-3">
                  <BaseBadge :tone="statusTone(currentStatus)">{{ currentStatus }}</BaseBadge>
                  <BaseButton
                    v-if="currentStatus === 'Running' || currentStatus === 'Pending'"
                    variant="danger"
                    size="sm"
                    :loading="isCancelling"
                    :disabled="isCancelling"
                    @click="cancelJob"
                  >
                    Cancel
                  </BaseButton>
                </div>
              </template>
              <div class="p-5 space-y-5">
                <!-- Progress bar -->
                <ProgressBar
                  :value="progressPercent"
                  :tone="progressTone"
                  :label="`${currentProcessed} / ${currentTotal} releases`"
                  show-value
                />

                <!-- Stats grid -->
                <div class="grid grid-cols-2 gap-4 sm:grid-cols-4">
                  <StatCard label="Processed" :value="currentProcessed" tone="accent" />
                  <StatCard label="Skipped" :value="currentSkipped" tone="neutral" />
                  <StatCard label="Failed" :value="currentFailed" :tone="currentFailed > 0 ? 'danger' : 'neutral'" />
                  <StatCard label="Bytes saved" :value="formatBytes(currentBytesSaved)" :tone="currentBytesSaved > 0 ? 'success' : 'neutral'" />
                </div>

                <!-- Timing -->
                <div v-if="currentStartedAt || currentCompletedAt" class="flex flex-wrap gap-4 text-xs text-ink-subtle">
                  <span v-if="currentStartedAt">Started: {{ formatDateTime(currentStartedAt) }}</span>
                  <span v-if="currentCompletedAt">Completed: {{ formatDateTime(currentCompletedAt) }}</span>
                </div>
              </div>
            </BaseCard>

            <!-- Tabs -->
            <Tabs :tabs="tabItems" v-model="activeTab" />

            <!-- Overview tab -->
            <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
              <div class="space-y-6 xl:col-span-2">
                <BaseCard>
                  <template #header>
                    <div>
                      <h2 class="text-lg font-semibold text-ink-strong">Store details</h2>
                      <p class="mt-1 text-sm text-ink-muted">Core metadata and storage configuration.</p>
                    </div>
                  </template>
                  <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <div class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Store ID</div>
                      <div class="mt-2 break-all font-mono text-xs text-ink-muted">{{ chunkStore.id }}</div>
                    </div>
                    <div class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Type</div>
                      <div class="mt-2 text-sm font-medium text-ink-strong">{{ chunkStore.type }}</div>
                    </div>
                    <div v-if="chunkStore.backendSettings?.localPath" class="rounded-control border border-hairline p-4 sm:col-span-2">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Storage path</div>
                      <div class="mt-2 break-all font-mono text-xs text-ink-muted">{{ chunkStore.backendSettings.localPath }}</div>
                    </div>
                  </div>
                </BaseCard>

                <!-- Chunker configuration -->
                <BaseCard v-if="chunkStore.chunker">
                  <template #header>
                    <h3 class="font-semibold text-ink-strong">Chunker configuration</h3>
                  </template>
                  <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                    <div class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Algorithm</div>
                      <div class="mt-1 text-sm font-semibold text-ink-strong">{{ chunkStore.chunker.type }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.minChunkSize != null" class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Min chunk</div>
                      <div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(chunkStore.chunker.minChunkSize) }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.avgChunkSize != null" class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Avg chunk</div>
                      <div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(chunkStore.chunker.avgChunkSize) }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.maxChunkSize != null" class="rounded-control border border-hairline p-4">
                      <div class="text-xs uppercase tracking-wide text-ink-subtle">Max chunk</div>
                      <div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(chunkStore.chunker.maxChunkSize) }}</div>
                    </div>
                  </div>
                </BaseCard>
              </div>

              <!-- Sidebar highlights -->
              <div class="space-y-6">
                <BaseCard>
                  <template #header>
                    <h3 class="font-semibold text-ink-strong">Stats</h3>
                  </template>
                  <div class="rounded-control border border-hairline p-4">
                    <div class="text-xs uppercase tracking-wide text-ink-subtle">Total chunks</div>
                    <div class="mt-1 text-lg font-bold text-ink-strong">
                      <span v-if="statsLoading" class="text-ink-subtle">…</span>
                      <span v-else-if="stats">{{ stats.totalChunks.toLocaleString() }}</span>
                      <span v-else>--</span>
                    </div>
                  </div>
                </BaseCard>
              </div>
            </section>

            <!-- Jobs tab -->
            <section v-if="activeTab === 'jobs'">
              <BaseCard :padded="false">
                <template #header>
                  <div>
                    <h2 class="text-lg font-semibold text-ink-strong">Upgrade jobs</h2>
                    <p class="mt-1 text-sm text-ink-muted">All upgrade jobs for this chunk store.</p>
                  </div>
                </template>
                <template #actions>
                  <BaseButton variant="secondary" size="sm" :loading="jobsLoading" :disabled="jobsLoading" @click="loadJobs">
                    Refresh
                  </BaseButton>
                </template>

                <div v-if="jobsLoading" class="flex items-center justify-center py-16">
                  <Spinner :size="32" color="var(--color-accent)" />
                </div>

                <EmptyState
                  v-else-if="jobs.length === 0"
                  :icon="IconArrowUp"
                  title="No upgrade jobs"
                  description="Start an upgrade to see job history here."
                />

                <div v-else class="divide-y divide-hairline">
                  <article v-for="job in jobs" :key="job.id" class="px-5 py-4">
                    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="text-sm font-semibold text-ink-strong truncate">Job {{ job.id.slice(0, 8) }}…</span>
                          <BaseBadge :tone="statusTone(job.status)">{{ job.status }}</BaseBadge>
                        </div>
                        <div class="mt-1 flex flex-wrap gap-3 text-xs text-ink-muted">
                          <span>v{{ job.targetSerializerVersion }}</span>
                          <span>{{ job.processedReleases }}/{{ job.totalReleases }} releases</span>
                          <span v-if="job.failedReleases > 0" class="text-danger">{{ job.failedReleases }} failed</span>
                          <span v-if="job.bytesSaved > 0" class="text-success">{{ formatBytes(job.bytesSaved) }} saved</span>
                          <span>Created {{ formatDateTime(job.createdAt) }}</span>
                        </div>
                      </div>
                    </div>
                  </article>
                </div>
              </BaseCard>
            </section>
          </template>
        </div>
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import Sidebar from '@/features/instance/components/InstanceSidebar.vue'
import Header from '@/shared/components/navigation/Header.vue'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import {
  Breadcrumbs,
  BaseCard,
  BaseButton,
  BaseBadge,
  StatCard,
  ProgressBar,
  Skeleton,
  EmptyState,
} from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'
import {
  getChunkStore,
  getChunkStoreStats,
  upgradeChunkStore,
  type ChunkStoreDetailDto,
  type ChunkStoreStatsDto,
} from '@/api/chunkStores'
import {
  listBackgroundJobs,
  cancelBackgroundJob,
  type BackgroundJobDto,
} from '@/api/backgroundJobs'
import { useBackgroundJobProgress, type BackgroundJobProgress } from '@/composables/useBackgroundJobProgress'
import {
  IconArrowUp,
  IconChevronRight,
  IconDatabase,
  IconHistory,
  IconInfoCircle,
} from '@tabler/icons-vue'

const route = useRoute()
const toast = useToast()
const storeId = computed(() => route.params.id as string)

const sidebarOpen = ref(false)

// -- Store detail --
const chunkStore = ref<ChunkStoreDetailDto | null>(null)
const loading = ref(false)
const loadError = ref<string | null>(null)

// -- Stats --
const stats = ref<ChunkStoreStatsDto | null>(null)
const statsLoading = ref(false)

// -- Tabs --
const tabs = [
  { key: 'overview', label: 'Overview', icon: IconInfoCircle },
  { key: 'jobs', label: 'Upgrade Jobs', icon: IconHistory },
]
const tabItems = tabs.map((t) => ({ id: t.key, label: t.label, icon: t.icon }))
const activeTab = ref('overview')

// -- Upgrade --
const upgradeStarting = ref(false)
const upgradeError = ref<string | null>(null)
const activeJob = ref<BackgroundJobDto | null>(null)
const isCancelling = ref(false)

// -- Subscription --
const { progress: activeProgress, subscribe: subscribeProgress, unsubscribe: unsubscribeProgress } = useBackgroundJobProgress()

// -- Jobs list --
const jobs = ref<BackgroundJobDto[]>([])
const jobsLoading = ref(false)

// -- Computed progress values (prefer live subscription, fall back to activeJob) --
const currentStatus = computed(() => activeProgress.value?.status ?? activeJob.value?.status ?? 'Unknown')
const currentTotal = computed(() => activeProgress.value?.totalReleases ?? activeJob.value?.upgradeProgress?.totalReleases ?? 0)
const currentProcessed = computed(() => activeProgress.value?.processedReleases ?? activeJob.value?.upgradeProgress?.processedReleases ?? 0)
const currentFailed = computed(() => activeProgress.value?.failedReleases ?? activeJob.value?.upgradeProgress?.failedReleases ?? 0)
const currentSkipped = computed(() => activeProgress.value?.skippedReleases ?? activeJob.value?.upgradeProgress?.skippedReleases ?? 0)
const currentBytesSaved = computed(() => activeProgress.value?.bytesSaved ?? activeJob.value?.upgradeProgress?.bytesSaved ?? 0)
const currentStartedAt = computed(() => activeProgress.value?.startedAt ?? activeJob.value?.startedAt ?? null)
const currentCompletedAt = computed(() => activeProgress.value?.completedAt ?? activeJob.value?.completedAt ?? null)

const isUpgrading = computed(() => {
  const s = currentStatus.value
  return s === 'Running' || s === 'Pending'
})

const progressPercent = computed(() => {
  const total = currentTotal.value
  if (total <= 0) return 0
  return Math.round((currentProcessed.value / total) * 100)
})

const progressTone = computed<'accent' | 'success' | 'warning' | 'danger'>(() => {
  const s = currentStatus.value
  if (s === 'Failed') return 'danger'
  if (s === 'Cancelled') return 'warning'
  if (s === 'Completed') return 'success'
  return 'accent'
})

// -- Actions --

async function loadDetail() {
  loading.value = true
  loadError.value = null
  try {
    chunkStore.value = await getChunkStore(storeId.value)
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Could not load chunk store.'
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    stats.value = await getChunkStoreStats(storeId.value)
  } catch {
    // stats are non-critical
  } finally {
    statsLoading.value = false
  }
}

async function loadJobs() {
  jobsLoading.value = true
  try {
    jobs.value = await listBackgroundJobs('ReleaseUpgrade', storeId.value)
    // If there's an active job running, track it
    const running = jobs.value.find(j => j.status === 'Running' || j.status === 'Pending')
    if (running) {
      activeJob.value = running
      subscribeProgress(running.id)
    }
  } catch {
    // non-critical
  } finally {
    jobsLoading.value = false
  }
}

async function startUpgrade() {
  upgradeStarting.value = true
  upgradeError.value = null
  try {
    const job = await upgradeChunkStore(storeId.value)
    activeJob.value = job
    subscribeProgress(job.id)
    toast.success('Upgrade started')
  } catch (e) {
    upgradeError.value = e instanceof Error ? e.message : 'Could not start upgrade.'
    toast.error(upgradeError.value)
  } finally {
    upgradeStarting.value = false
  }
}

async function cancelJob() {
  const jobId = activeJob.value?.id
  if (!jobId) return
  isCancelling.value = true
  try {
    await cancelBackgroundJob(jobId)
    toast.success('Job cancelled')
  } catch (e) {
    upgradeError.value = e instanceof Error ? e.message : 'Could not cancel job.'
    toast.error(upgradeError.value)
  } finally {
    isCancelling.value = false
  }
}

// -- Formatting helpers --

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '--'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0
  let val = bytes
  while (val >= 1024 && i < units.length - 1) {
    val /= 1024
    i += 1
  }
  return `${val % 1 === 0 ? val : val.toFixed(1)} ${units[i]}`
}

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '--'
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

function statusTone(status: string): 'neutral' | 'accent' | 'success' | 'warning' | 'danger' {
  switch (status) {
    case 'Completed':
      return 'success'
    case 'Running':
      return 'accent'
    case 'Pending':
      return 'warning'
    case 'Failed':
      return 'danger'
    case 'Cancelled':
      return 'neutral'
    default:
      return 'neutral'
  }
}

// -- Watchers --

watch(activeTab, (tab) => {
  if (tab === 'jobs') loadJobs()
})

watch(storeId, () => {
  chunkStore.value = null
  stats.value = null
  jobs.value = []
  activeJob.value = null
  unsubscribeProgress()
  activeTab.value = 'overview'
  if (storeId.value) {
    loadDetail()
    loadStats()
    loadJobs()
  }
})

// -- Lifecycle --

onMounted(() => {
  if (storeId.value) {
    loadDetail()
    loadStats()
    loadJobs()
  }
})
</script>
