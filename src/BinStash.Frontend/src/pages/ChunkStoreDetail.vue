<template>
  <div class="flex h-dvh overflow-hidden">
    <Sidebar :sidebarOpen="sidebarOpen" @close-sidebar="sidebarOpen = false" />

    <div class="relative flex flex-col flex-1 overflow-y-auto overflow-x-hidden">
      <Header :sidebarOpen="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" />

      <main class="grow">
        <div class="px-4 sm:px-6 lg:px-8 py-8 w-full max-w-384 mx-auto space-y-6">

          <!-- Breadcrumb -->
          <nav class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400">
            <router-link to="/chunk-stores" class="hover:text-violet-500 transition">Chunk Stores</router-link>
            <IconChevronRight class="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" />
            <span class="text-gray-700 dark:text-gray-200 font-medium truncate">{{ chunkStore?.name ?? '...' }}</span>
          </nav>

          <!-- Loading skeleton -->
          <div v-if="loading" class="animate-pulse space-y-6">
            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 p-6 space-y-4">
              <div class="h-3 w-32 rounded bg-gray-200 dark:bg-gray-700" />
              <div class="h-10 w-64 rounded bg-gray-200 dark:bg-gray-700" />
              <div class="h-16 rounded-xl bg-gray-200 dark:bg-gray-700" />
            </div>
          </div>

          <!-- Error state -->
          <div v-else-if="loadError" class="rounded-2xl border border-rose-200 dark:border-rose-500/20 bg-rose-50 dark:bg-rose-500/10 px-5 py-4 text-sm text-rose-700 dark:text-rose-300">
            {{ loadError }}
          </div>

          <!-- Main content -->
          <template v-else-if="chunkStore">

            <!-- Header card -->
            <section class="overflow-hidden rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs">
              <div class="h-1 bg-linear-to-r from-teal-400 via-violet-500 to-indigo-500" />
              <div class="p-5 lg:p-6">
                <div class="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
                  <div class="min-w-0 space-y-4">
                    <div class="flex items-start gap-3">
                      <div class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-teal-100 text-teal-600 dark:bg-teal-500/20 dark:text-teal-300">
                        <IconDatabase class="h-5.5 w-5.5" />
                      </div>
                      <div class="min-w-0">
                        <h1 class="truncate text-2xl font-bold text-gray-900 dark:text-white">{{ chunkStore.name }}</h1>
                        <p class="mt-1.5 text-sm text-gray-500 dark:text-gray-400">
                          {{ chunkStore.type }} chunk store
                          <span v-if="chunkStore.backendSettings?.localPath"> &middot; {{ chunkStore.backendSettings.localPath }}</span>
                        </p>
                      </div>
                    </div>
                  </div>
                  <div class="flex items-center gap-2">
                    <button
                      type="button"
                      class="btn bg-violet-500 text-white hover:bg-violet-600 disabled:opacity-50"
                      :disabled="isUpgrading || upgradeStarting"
                      @click="startUpgrade"
                    >
                      <span v-if="upgradeStarting">Starting...</span>
                      <span v-else-if="isUpgrading">Upgrade running...</span>
                      <span v-else class="inline-flex items-center gap-2">
                        <IconArrowUp class="h-4 w-4" />
                        Upgrade Releases
                      </span>
                    </button>
                  </div>
                </div>
              </div>
            </section>

            <!-- Upgrade error -->
            <div v-if="upgradeError" class="rounded-2xl border border-rose-200 dark:border-rose-500/20 bg-rose-50 dark:bg-rose-500/10 px-5 py-4 text-sm text-rose-700 dark:text-rose-300">
              {{ upgradeError }}
            </div>

            <!-- Real-time upgrade progress -->
            <section v-if="activeProgress || activeJob" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4 flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-violet-100 text-violet-600 dark:bg-violet-500/20 dark:text-violet-300">
                    <IconArrowUp class="h-4 w-4" />
                  </div>
                  <h2 class="font-semibold text-gray-900 dark:text-white">Upgrade Progress</h2>
                </div>
                <div class="flex items-center gap-3">
                  <span class="inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold" :class="statusBadgeClasses(currentStatus)">
                    {{ currentStatus }}
                  </span>
                  <button
                    v-if="currentStatus === 'Running' || currentStatus === 'Pending'"
                    type="button"
                    class="btn text-sm border-rose-200 text-rose-600 hover:border-rose-300 hover:text-rose-700 dark:border-rose-500/30 dark:text-rose-400"
                    :disabled="isCancelling"
                    @click="cancelJob"
                  >
                    {{ isCancelling ? 'Cancelling...' : 'Cancel' }}
                  </button>
                </div>
              </div>
              <div class="p-5 space-y-5">
                <!-- Progress bar -->
                <div>
                  <div class="flex items-center justify-between text-sm mb-2">
                    <span class="text-gray-600 dark:text-gray-300">
                      {{ currentProcessed }} / {{ currentTotal }} releases
                    </span>
                    <span class="font-medium text-gray-900 dark:text-white">{{ progressPercent }}%</span>
                  </div>
                  <div class="w-full h-3 bg-gray-200 dark:bg-gray-700 rounded-full overflow-hidden">
                    <div
                      class="h-full rounded-full transition-all duration-300 ease-out"
                      :class="progressBarColor"
                      :style="{ width: `${progressPercent}%` }"
                    />
                  </div>
                </div>

                <!-- Stats grid -->
                <div class="grid grid-cols-2 gap-4 sm:grid-cols-4">
                  <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                    <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Processed</div>
                    <div class="mt-1 text-lg font-bold text-gray-900 dark:text-white">{{ currentProcessed }}</div>
                  </div>
                  <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                    <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Skipped</div>
                    <div class="mt-1 text-lg font-bold text-gray-900 dark:text-white">{{ currentSkipped }}</div>
                  </div>
                  <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                    <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Failed</div>
                    <div class="mt-1 text-lg font-bold" :class="currentFailed > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-gray-900 dark:text-white'">{{ currentFailed }}</div>
                  </div>
                  <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                    <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Bytes saved</div>
                    <div class="mt-1 text-lg font-bold" :class="currentBytesSaved > 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-gray-900 dark:text-white'">{{ formatBytes(currentBytesSaved) }}</div>
                  </div>
                </div>

                <!-- Timing -->
                <div v-if="currentStartedAt || currentCompletedAt" class="flex flex-wrap gap-4 text-xs text-gray-500 dark:text-gray-400">
                  <span v-if="currentStartedAt">Started: {{ formatDateTime(currentStartedAt) }}</span>
                  <span v-if="currentCompletedAt">Completed: {{ formatDateTime(currentCompletedAt) }}</span>
                </div>
              </div>
            </section>

            <!-- Tabs -->
            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 p-1 shadow-xs">
              <nav class="flex flex-wrap gap-1">
                <button
                  v-for="tab in tabs"
                  :key="tab.key"
                  type="button"
                  class="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-medium transition"
                  :class="activeTab === tab.key
                    ? 'bg-violet-500 text-white shadow-xs'
                    : 'text-gray-500 hover:bg-gray-100 hover:text-gray-800 dark:text-gray-400 dark:hover:bg-gray-700/60 dark:hover:text-gray-100'"
                  @click="activeTab = tab.key"
                >
                  <component :is="tab.icon" class="h-4 w-4" />
                  {{ tab.label }}
                </button>
              </nav>
            </div>

            <!-- Overview tab -->
            <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
              <div class="space-y-6 xl:col-span-2">
                <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
                  <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                    <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Store details</h2>
                    <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Core metadata and storage configuration.</p>
                  </div>
                  <div class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
                    <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Store ID</div>
                      <div class="mt-2 break-all font-mono text-xs text-gray-700 dark:text-gray-200">{{ chunkStore.id }}</div>
                    </div>
                    <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Type</div>
                      <div class="mt-2 text-sm font-medium text-gray-900 dark:text-white">{{ chunkStore.type }}</div>
                    </div>
                    <div v-if="chunkStore.backendSettings?.localPath" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4 sm:col-span-2">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Storage path</div>
                      <div class="mt-2 break-all font-mono text-xs text-gray-700 dark:text-gray-200">{{ chunkStore.backendSettings.localPath }}</div>
                    </div>
                  </div>
                </div>

                <!-- Chunker configuration -->
                <div v-if="chunkStore.chunker" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
                  <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                    <h3 class="font-semibold text-gray-900 dark:text-white">Chunker configuration</h3>
                  </div>
                  <div class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2 xl:grid-cols-4">
                    <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Algorithm</div>
                      <div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ chunkStore.chunker.type }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.minChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Min chunk</div>
                      <div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(chunkStore.chunker.minChunkSize) }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.avgChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Avg chunk</div>
                      <div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(chunkStore.chunker.avgChunkSize) }}</div>
                    </div>
                    <div v-if="chunkStore.chunker.maxChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Max chunk</div>
                      <div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(chunkStore.chunker.maxChunkSize) }}</div>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Sidebar highlights -->
              <div class="space-y-6">
                <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
                  <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                    <h3 class="font-semibold text-gray-900 dark:text-white">Stats</h3>
                  </div>
                  <div class="space-y-4 p-5">
                    <div class="rounded-xl bg-gray-50 p-4 dark:bg-gray-900/30">
                      <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Total chunks</div>
                      <div class="mt-1 text-lg font-bold text-gray-900 dark:text-white">
                        <span v-if="statsLoading" class="text-gray-400">...</span>
                        <span v-else-if="stats">{{ stats.totalChunks.toLocaleString() }}</span>
                        <span v-else>--</span>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </section>

            <!-- Jobs tab -->
            <section v-if="activeTab === 'jobs'">
              <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
                <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4 flex items-center justify-between">
                  <div>
                    <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Upgrade jobs</h2>
                    <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">All upgrade jobs for this chunk store.</p>
                  </div>
                  <button
                    type="button"
                    class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600 text-sm"
                    :disabled="jobsLoading"
                    @click="loadJobs"
                  >
                    Refresh
                  </button>
                </div>

                <div v-if="jobsLoading" class="flex items-center justify-center py-16">
                  <div class="h-8 w-8 animate-spin rounded-full border-2 border-violet-500/20 border-t-violet-500" />
                </div>

                <div v-else-if="jobs.length === 0" class="px-6 py-16 text-center">
                  <div class="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-gray-100 text-gray-400 dark:bg-gray-700/50 dark:text-gray-500">
                    <IconArrowUp class="h-7 w-7" />
                  </div>
                  <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-200">No upgrade jobs</h3>
                  <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Start an upgrade to see job history here.</p>
                </div>

                <div v-else class="divide-y divide-gray-100 dark:divide-gray-700/60">
                  <article v-for="job in jobs" :key="job.id" class="px-5 py-4">
                    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="text-sm font-semibold text-gray-900 dark:text-white truncate">Job {{ job.id.slice(0, 8) }}...</span>
                          <span class="inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold" :class="statusBadgeClasses(job.status)">
                            {{ job.status }}
                          </span>
                        </div>
                        <div class="mt-1 flex flex-wrap gap-3 text-xs text-gray-500 dark:text-gray-400">
                          <span>v{{ job.targetSerializerVersion }}</span>
                          <span>{{ job.processedReleases }}/{{ job.totalReleases }} releases</span>
                          <span v-if="job.failedReleases > 0" class="text-rose-500">{{ job.failedReleases }} failed</span>
                          <span v-if="job.bytesSaved > 0" class="text-emerald-500">{{ formatBytes(job.bytesSaved) }} saved</span>
                          <span>Created {{ formatDateTime(job.createdAt) }}</span>
                        </div>
                      </div>
                    </div>
                  </article>
                </div>
              </div>
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

const progressBarColor = computed(() => {
  const s = currentStatus.value
  if (s === 'Failed') return 'bg-rose-500'
  if (s === 'Cancelled') return 'bg-amber-500'
  if (s === 'Completed') return 'bg-emerald-500'
  return 'bg-violet-500'
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
  } catch (e) {
    upgradeError.value = e instanceof Error ? e.message : 'Could not start upgrade.'
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
  } catch (e) {
    upgradeError.value = e instanceof Error ? e.message : 'Could not cancel job.'
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

function statusBadgeClasses(status: string): string {
  switch (status) {
    case 'Completed':
      return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300'
    case 'Running':
      return 'bg-violet-100 text-violet-700 dark:bg-violet-500/20 dark:text-violet-300'
    case 'Pending':
      return 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300'
    case 'Failed':
      return 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-300'
    case 'Cancelled':
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/60 dark:text-gray-300'
    default:
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/60 dark:text-gray-300'
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
