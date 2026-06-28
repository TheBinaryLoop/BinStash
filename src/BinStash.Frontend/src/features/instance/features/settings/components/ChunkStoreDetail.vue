<template>
  <div class="space-y-6">
    <!-- Back button + title -->
    <div class="flex items-center gap-3">
      <button
        @click="$emit('back')"
        class="flex items-center gap-1.5 text-sm text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 transition"
      >
        <IconArrowLeft class="w-4 h-4" />
        Back
      </button>
      <span class="text-gray-300 dark:text-gray-600">/</span>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">{{ store?.name ?? '…' }}</h2>
      <span v-if="store" class="text-xs font-mono bg-gray-100 dark:bg-gray-700 text-gray-500 dark:text-gray-400 px-2 py-0.5 rounded">
        {{ store.type }}
      </span>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <svg class="animate-spin w-5 h-5" viewBox="0 0 24 24" fill="none">
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
        <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
      </svg>
      <span>Loading chunk store…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-4 text-sm text-rose-700 dark:text-rose-400">
      {{ error }}
    </div>

    <template v-else-if="store">
      <!-- Inner tab nav -->
      <div class="flex gap-1 border-b border-gray-200 dark:border-gray-700">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          @click="activeTab = tab.id"
          class="px-4 py-2 text-sm font-medium border-b-2 -mb-px transition"
          :class="activeTab === tab.id
            ? 'border-violet-500 text-violet-600 dark:text-violet-400'
            : 'border-transparent text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200'"
        >
          <span class="flex items-center gap-1.5">
            <component :is="tab.icon" class="w-4 h-4" />
            {{ tab.label }}
            <span v-if="tab.id === 'danger'" class="text-xs bg-rose-100 dark:bg-rose-500/20 text-rose-600 dark:text-rose-400 px-1.5 py-0.5 rounded-full font-semibold">!</span>
          </span>
        </button>
      </div>

      <!-- ── Stats tab ── -->
      <div v-if="activeTab === 'stats'" class="space-y-6">
        <div class="bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-xl p-4 text-sm text-amber-700 dark:text-amber-400 flex items-center gap-2">
          <IconAlertCircle class="w-4 h-4 shrink-0" />
          Statistics are not yet available from the API. The charts below show placeholder data.
        </div>

        <!-- Stat cards -->
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-4">
            <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide mb-2">Total Chunks</div>
            <div class="text-2xl font-bold text-gray-800 dark:text-gray-100">{{ stats?.totalChunks ?? '—' }}</div>
            <div class="text-xs text-gray-500 dark:text-gray-400 mt-1">Placeholder</div>
          </div>
          <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-4">
            <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide mb-2">Storage Used</div>
            <div class="text-2xl font-bold text-gray-800 dark:text-gray-100">—</div>
            <div class="text-xs text-gray-500 dark:text-gray-400 mt-1">Placeholder</div>
          </div>
          <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-4">
            <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide mb-2">Dedup Ratio</div>
            <div class="text-2xl font-bold text-gray-800 dark:text-gray-100">—</div>
            <div class="text-xs text-gray-500 dark:text-gray-400 mt-1">Placeholder</div>
          </div>
        </div>

        <!-- Placeholder charts -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Chunks over time -->
          <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5">
            <div class="font-semibold text-gray-800 dark:text-gray-100 mb-1 text-sm">Chunks Ingested (last 7 days)</div>
            <div class="text-xs text-gray-400 dark:text-gray-500 mb-4">Placeholder data — real stats coming soon</div>
            <div class="flex items-end gap-1.5 h-24">
              <div
                v-for="(h, i) in dummyBarData"
                :key="i"
                class="flex-1 bg-violet-200 dark:bg-violet-500/30 rounded-t"
                :style="{ height: h + '%' }"
              ></div>
            </div>
            <div class="flex justify-between text-xs text-gray-400 dark:text-gray-500 mt-1.5">
              <span>Mon</span><span>Tue</span><span>Wed</span><span>Thu</span><span>Fri</span><span>Sat</span><span>Sun</span>
            </div>
          </div>

          <!-- Storage over time -->
          <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5">
            <div class="font-semibold text-gray-800 dark:text-gray-100 mb-1 text-sm">Storage Growth (last 7 days)</div>
            <div class="text-xs text-gray-400 dark:text-gray-500 mb-4">Placeholder data — real stats coming soon</div>
            <div class="flex items-end gap-1.5 h-24">
              <div
                v-for="(h, i) in dummyGrowthData"
                :key="i"
                class="flex-1 bg-sky-200 dark:bg-sky-500/30 rounded-t"
                :style="{ height: h + '%' }"
              ></div>
            </div>
            <div class="flex justify-between text-xs text-gray-400 dark:text-gray-500 mt-1.5">
              <span>Mon</span><span>Tue</span><span>Wed</span><span>Thu</span><span>Fri</span><span>Sat</span><span>Sun</span>
            </div>
          </div>
        </div>

        <!-- Chunker config -->
        <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5">
          <div class="font-semibold text-gray-800 dark:text-gray-100 mb-3 text-sm">Chunker Configuration</div>
          <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
            <div>
              <div class="text-xs text-gray-500 dark:text-gray-400 mb-0.5">Type</div>
              <div class="font-medium text-gray-800 dark:text-gray-100 font-mono">{{ store.chunker?.type ?? '—' }}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 dark:text-gray-400 mb-0.5">Min Chunk</div>
              <div class="font-medium text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker?.minChunkSize) }}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 dark:text-gray-400 mb-0.5">Avg Chunk</div>
              <div class="font-medium text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker?.avgChunkSize) }}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 dark:text-gray-400 mb-0.5">Max Chunk</div>
              <div class="font-medium text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker?.maxChunkSize) }}</div>
            </div>
          </div>
        </div>
      </div>

      <!-- ── Settings tab ── -->
      <div v-else-if="activeTab === 'settings'" class="space-y-5">
        <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5">
          <h3 class="font-semibold text-gray-800 dark:text-gray-100 mb-4 text-sm">Chunk Store Details</h3>
          <div class="space-y-4 max-w-lg">
            <div>
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">ID</label>
              <div class="font-mono text-xs text-gray-600 dark:text-gray-400 bg-gray-50 dark:bg-gray-700/50 px-3 py-2 rounded-lg select-all">{{ store.id }}</div>
            </div>
            <div>
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Name</label>
              <div class="text-sm font-medium text-gray-800 dark:text-gray-100">{{ store.name }}</div>
            </div>
            <div>
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Type</label>
              <div class="text-sm font-medium text-gray-800 dark:text-gray-100 font-mono">{{ store.type }}</div>
            </div>
          </div>
        </div>

        <div v-if="store.chunker" class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5">
          <h3 class="font-semibold text-gray-800 dark:text-gray-100 mb-4 text-sm">Chunker Settings</h3>
          <div class="grid grid-cols-2 gap-4 max-w-lg text-sm">
            <div>
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Type</label>
              <div class="font-mono text-gray-800 dark:text-gray-100">{{ store.chunker.type }}</div>
            </div>
            <div v-if="store.chunker.minChunkSize != null">
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Min Chunk Size</label>
              <div class="text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker.minChunkSize) }}</div>
            </div>
            <div v-if="store.chunker.avgChunkSize != null">
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Avg Chunk Size</label>
              <div class="text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker.avgChunkSize) }}</div>
            </div>
            <div v-if="store.chunker.maxChunkSize != null">
              <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Max Chunk Size</label>
              <div class="text-gray-800 dark:text-gray-100">{{ formatBytes(store.chunker.maxChunkSize) }}</div>
            </div>
          </div>
        </div>
      </div>

      <!-- ── Danger Zone tab ── -->
      <div v-else-if="activeTab === 'danger'" class="space-y-4">
        <!-- Warning banner -->
        <div class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-5 flex items-start gap-3">
          <IconAlertTriangle class="text-rose-500 w-5 h-5 shrink-0 mt-0.5" />
          <div>
            <h3 class="font-semibold text-rose-800 dark:text-rose-300 text-sm mb-1">Danger Zone</h3>
            <p class="text-sm text-rose-700 dark:text-rose-400">
              These operations are destructive or long-running. They cannot be undone. Proceed with caution.
            </p>
          </div>
        </div>

        <!-- Rebuild -->
        <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5 border border-rose-200 dark:border-rose-500/30">
          <div class="flex items-start justify-between gap-4">
            <div>
              <h4 class="font-semibold text-gray-800 dark:text-gray-100 text-sm">Rebuild Chunk Store</h4>
              <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">
                Scans the underlying storage and rewrites the pack and index files. Use this to recover from corruption or after manual storage changes.
              </p>
            </div>
            <button
              @click="confirmAction('rebuild')"
              :disabled="actionInProgress"
              class="shrink-0 btn bg-rose-500 hover:bg-rose-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
            >
              Rebuild
            </button>
          </div>
        </div>

        <!-- Upgrade -->
        <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-5 border border-rose-200 dark:border-rose-500/30">
          <div class="flex items-start justify-between gap-4">
            <div>
              <h4 class="font-semibold text-gray-800 dark:text-gray-100 text-sm">Upgrade Releases</h4>
              <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">
                Upgrades all releases in this chunk store to the latest format version. This may take a long time on large stores.
              </p>
            </div>
            <button
              @click="confirmAction('upgrade')"
              :disabled="actionInProgress"
              class="shrink-0 btn bg-rose-500 hover:bg-rose-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
            >
              Upgrade
            </button>
          </div>
        </div>

        <!-- Action result -->
        <div v-if="actionError" class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-3 text-sm text-rose-700 dark:text-rose-400">
          {{ actionError }}
        </div>
        <div v-if="actionSuccess" class="bg-green-50 dark:bg-green-500/10 border border-green-200 dark:border-green-500/30 rounded-xl p-3 text-sm text-green-700 dark:text-green-400 flex items-center gap-2">
          <IconCircleCheck class="w-4 h-4 shrink-0" />
          {{ actionSuccess }}
        </div>
      </div>
    </template>

    <!-- Confirm modal -->
    <div
      v-if="confirmModal.visible"
      class="fixed inset-0 z-50 flex items-center justify-center bg-gray-900/60 backdrop-blur-sm"
    >
      <div class="bg-white dark:bg-gray-800 rounded-xl shadow-xl p-6 max-w-sm w-full mx-4">
        <div class="flex items-center gap-3 mb-3">
          <div class="w-10 h-10 rounded-full bg-rose-100 dark:bg-rose-500/20 flex items-center justify-center shrink-0">
            <IconAlertTriangle class="text-rose-500 w-5 h-5" />
          </div>
          <h3 class="font-semibold text-gray-800 dark:text-gray-100">Confirm {{ confirmModal.action === 'rebuild' ? 'Rebuild' : 'Upgrade' }}</h3>
        </div>
        <p class="text-sm text-gray-600 dark:text-gray-400 mb-5">
          Are you sure you want to {{ confirmModal.action === 'rebuild' ? 'rebuild' : 'upgrade' }} this chunk store?
          This operation may take a long time and cannot be undone.
        </p>
        <div class="flex gap-3">
          <button
            @click="runAction"
            :disabled="actionInProgress"
            class="flex-1 btn bg-rose-500 hover:bg-rose-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            <svg v-if="actionInProgress" class="animate-spin w-4 h-4 inline mr-1" viewBox="0 0 24 24" fill="none">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
            </svg>
            {{ actionInProgress ? 'Running…' : 'Confirm' }}
          </button>
          <button
            @click="confirmModal.visible = false"
            :disabled="actionInProgress"
            class="flex-1 btn bg-white dark:bg-gray-700 border border-gray-200 dark:border-gray-600 text-gray-700 dark:text-gray-300 hover:border-gray-300 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, onMounted } from 'vue'
import {
  IconArrowLeft,
  IconAlertCircle,
  IconAlertTriangle,
  IconCircleCheck,
  IconChartBar,
  IconAdjustmentsHorizontal,
  IconFlame,
} from '@tabler/icons-vue'
import {
  getChunkStore,
  getChunkStoreStats,
  rebuildChunkStore,
  upgradeChunkStore,
  type ChunkStoreDetailDto,
  type ChunkStoreStatsDto,
} from '@/api/chunkStores'

const props = defineProps<{ storeId: string }>()
defineEmits<{ (e: 'back'): void }>()

const loading = ref(true)
const error = ref<string | null>(null)
const store = ref<ChunkStoreDetailDto | null>(null)
const stats = ref<ChunkStoreStatsDto | null>(null)
const activeTab = ref<'stats' | 'settings' | 'danger'>('stats')

const tabs = [
  { id: 'stats', label: 'Statistics', icon: IconChartBar },
  { id: 'settings', label: 'Settings', icon: IconAdjustmentsHorizontal },
  { id: 'danger', label: 'Danger Zone', icon: IconFlame },
] as const

// Dummy chart data
const dummyBarData = [45, 72, 38, 85, 60, 30, 55]
const dummyGrowthData = [20, 35, 42, 50, 58, 62, 70]

const actionInProgress = ref(false)
const actionError = ref<string | null>(null)
const actionSuccess = ref<string | null>(null)
const confirmModal = reactive({ visible: false, action: '' as 'rebuild' | 'upgrade' })

async function load() {
  loading.value = true
  error.value = null
  try {
    store.value = await getChunkStore(props.storeId)
    stats.value = await getChunkStoreStats(props.storeId)
  } catch (e: any) {
    error.value = e.message || 'Failed to load chunk store.'
  } finally {
    loading.value = false
  }
}

function confirmAction(action: 'rebuild' | 'upgrade') {
  actionError.value = null
  actionSuccess.value = null
  confirmModal.action = action
  confirmModal.visible = true
}

async function runAction() {
  actionInProgress.value = true
  actionError.value = null
  actionSuccess.value = null
  try {
    if (confirmModal.action === 'rebuild') {
      await rebuildChunkStore(props.storeId)
      actionSuccess.value = 'Rebuild completed successfully.'
    } else {
      await upgradeChunkStore(props.storeId)
      actionSuccess.value = 'Upgrade completed successfully.'
    }
  } catch (e: any) {
    actionError.value = e.message || `Failed to ${confirmModal.action} chunk store.`
  } finally {
    actionInProgress.value = false
    confirmModal.visible = false
  }
}

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '—'
  if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
  if (bytes >= 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return bytes + ' B'
}

onMounted(load)
</script>