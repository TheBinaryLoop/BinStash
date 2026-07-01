<template>
  <div class="space-y-6">
    <!-- Back button + title -->
    <div class="flex items-center gap-3">
      <BaseButton variant="ghost" size="sm" :icon="IconArrowLeft" @click="$emit('back')">
        Back
      </BaseButton>
      <span class="text-ink-subtle">/</span>
      <h2 class="text-lg font-semibold text-ink-strong">{{ store?.name ?? '…' }}</h2>
      <BaseBadge v-if="store" tone="neutral" class="font-mono">{{ store.type }}</BaseBadge>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading chunk store…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      {{ error }}
    </div>

    <template v-else-if="store">
      <!-- Inner tab nav -->
      <div class="flex gap-1 border-b border-hairline">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          @click="activeTab = tab.id"
          class="-mb-px border-b-2 px-4 py-2 text-sm font-medium transition"
          :class="activeTab === tab.id
            ? 'border-accent text-accent'
            : 'border-transparent text-ink-muted hover:text-ink-strong'"
        >
          <span class="flex items-center gap-1.5">
            <component :is="tab.icon" class="h-4 w-4" />
            {{ tab.label }}
            <BaseBadge v-if="tab.id === 'danger'" tone="danger" size="sm">!</BaseBadge>
          </span>
        </button>
      </div>

      <!-- ── Stats tab ── -->
      <div v-if="activeTab === 'stats'" class="space-y-6">
        <div class="flex items-center gap-2 rounded-card border border-warning/25 bg-warning-soft px-4 py-3 text-sm text-warning">
          <IconAlertCircle class="h-4 w-4 shrink-0" />
          Statistics are not yet available from the API. The charts below show placeholder data.
        </div>

        <!-- Stat cards -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <BaseCard density="compact">
            <div class="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-subtle">Total Chunks</div>
            <div class="text-2xl font-bold text-ink-strong">{{ stats?.totalChunks ?? '—' }}</div>
            <div class="mt-1 text-xs text-ink-muted">Placeholder</div>
          </BaseCard>
          <BaseCard density="compact">
            <div class="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-subtle">Storage Used</div>
            <div class="text-2xl font-bold text-ink-strong">—</div>
            <div class="mt-1 text-xs text-ink-muted">Placeholder</div>
          </BaseCard>
          <BaseCard density="compact">
            <div class="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-subtle">Dedup Ratio</div>
            <div class="text-2xl font-bold text-ink-strong">—</div>
            <div class="mt-1 text-xs text-ink-muted">Placeholder</div>
          </BaseCard>
        </div>

        <!-- Placeholder charts -->
        <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
          <!-- Chunks over time -->
          <BaseCard>
            <div class="mb-1 text-sm font-semibold text-ink-strong">Chunks Ingested (last 7 days)</div>
            <div class="mb-4 text-xs text-ink-subtle">Placeholder data — real stats coming soon</div>
            <div class="flex h-24 items-end gap-1.5">
              <div
                v-for="(h, i) in dummyBarData"
                :key="i"
                class="flex-1 rounded-t bg-accent-soft"
                :style="{ height: h + '%' }"
              ></div>
            </div>
            <div class="mt-1.5 flex justify-between text-xs text-ink-subtle">
              <span>Mon</span><span>Tue</span><span>Wed</span><span>Thu</span><span>Fri</span><span>Sat</span><span>Sun</span>
            </div>
          </BaseCard>

          <!-- Storage over time -->
          <BaseCard>
            <div class="mb-1 text-sm font-semibold text-ink-strong">Storage Growth (last 7 days)</div>
            <div class="mb-4 text-xs text-ink-subtle">Placeholder data — real stats coming soon</div>
            <div class="flex h-24 items-end gap-1.5">
              <div
                v-for="(h, i) in dummyGrowthData"
                :key="i"
                class="flex-1 rounded-t bg-success-soft"
                :style="{ height: h + '%' }"
              ></div>
            </div>
            <div class="mt-1.5 flex justify-between text-xs text-ink-subtle">
              <span>Mon</span><span>Tue</span><span>Wed</span><span>Thu</span><span>Fri</span><span>Sat</span><span>Sun</span>
            </div>
          </BaseCard>
        </div>

        <!-- Chunker config -->
        <BaseCard>
          <div class="mb-3 text-sm font-semibold text-ink-strong">Chunker Configuration</div>
          <div class="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
            <div>
              <div class="mb-0.5 text-xs text-ink-muted">Type</div>
              <div class="font-mono font-medium text-ink-strong">{{ store.chunker?.type ?? '—' }}</div>
            </div>
            <div>
              <div class="mb-0.5 text-xs text-ink-muted">Min Chunk</div>
              <div class="font-medium text-ink-strong">{{ formatBytes(store.chunker?.minChunkSize) }}</div>
            </div>
            <div>
              <div class="mb-0.5 text-xs text-ink-muted">Avg Chunk</div>
              <div class="font-medium text-ink-strong">{{ formatBytes(store.chunker?.avgChunkSize) }}</div>
            </div>
            <div>
              <div class="mb-0.5 text-xs text-ink-muted">Max Chunk</div>
              <div class="font-medium text-ink-strong">{{ formatBytes(store.chunker?.maxChunkSize) }}</div>
            </div>
          </div>
        </BaseCard>
      </div>

      <!-- ── Settings tab ── -->
      <div v-else-if="activeTab === 'settings'" class="space-y-5">
        <BaseCard>
          <h3 class="mb-4 text-sm font-semibold text-ink-strong">Chunk Store Details</h3>
          <div class="max-w-lg space-y-4">
            <div>
              <label class="mb-1 block text-xs font-medium text-ink-subtle">ID</label>
              <div class="select-all rounded-control bg-raised px-3 py-2 font-mono text-xs text-ink-muted">{{ store.id }}</div>
            </div>
            <div>
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Name</label>
              <div class="text-sm font-medium text-ink-strong">{{ store.name }}</div>
            </div>
            <div>
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Type</label>
              <div class="font-mono text-sm font-medium text-ink-strong">{{ store.type }}</div>
            </div>
          </div>
        </BaseCard>

        <BaseCard v-if="store.chunker">
          <h3 class="mb-4 text-sm font-semibold text-ink-strong">Chunker Settings</h3>
          <div class="grid max-w-lg grid-cols-2 gap-4 text-sm">
            <div>
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Type</label>
              <div class="font-mono text-ink-strong">{{ store.chunker.type }}</div>
            </div>
            <div v-if="store.chunker.minChunkSize != null">
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Min Chunk Size</label>
              <div class="text-ink-strong">{{ formatBytes(store.chunker.minChunkSize) }}</div>
            </div>
            <div v-if="store.chunker.avgChunkSize != null">
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Avg Chunk Size</label>
              <div class="text-ink-strong">{{ formatBytes(store.chunker.avgChunkSize) }}</div>
            </div>
            <div v-if="store.chunker.maxChunkSize != null">
              <label class="mb-1 block text-xs font-medium text-ink-subtle">Max Chunk Size</label>
              <div class="text-ink-strong">{{ formatBytes(store.chunker.maxChunkSize) }}</div>
            </div>
          </div>
        </BaseCard>
      </div>

      <!-- ── Danger Zone tab ── -->
      <div v-else-if="activeTab === 'danger'" class="space-y-4">
        <!-- Warning banner -->
        <div class="flex items-start gap-3 rounded-card border border-danger/20 bg-danger-soft p-5">
          <IconAlertTriangle class="mt-0.5 h-5 w-5 shrink-0 text-danger" />
          <div>
            <h3 class="mb-1 text-sm font-semibold text-danger">Danger Zone</h3>
            <p class="text-sm text-danger/90">
              These operations are destructive or long-running. They cannot be undone. Proceed with caution.
            </p>
          </div>
        </div>

        <!-- Rebuild -->
        <div class="rounded-card border border-danger/20 bg-card p-5">
          <div class="flex items-start justify-between gap-4">
            <div>
              <h4 class="text-sm font-semibold text-ink-strong">Rebuild Chunk Store</h4>
              <p class="mt-1 text-sm text-ink-muted">
                Scans the underlying storage and rewrites the pack and index files. Use this to recover from corruption or after manual storage changes.
              </p>
            </div>
            <BaseButton variant="danger" class="shrink-0" :disabled="actionInProgress" @click="confirmAction('rebuild')">
              Rebuild
            </BaseButton>
          </div>
        </div>

        <!-- Upgrade -->
        <div class="rounded-card border border-danger/20 bg-card p-5">
          <div class="flex items-start justify-between gap-4">
            <div>
              <h4 class="text-sm font-semibold text-ink-strong">Upgrade Releases</h4>
              <p class="mt-1 text-sm text-ink-muted">
                Upgrades all releases in this chunk store to the latest format version. This may take a long time on large stores.
              </p>
            </div>
            <BaseButton variant="danger" class="shrink-0" :disabled="actionInProgress" @click="confirmAction('upgrade')">
              Upgrade
            </BaseButton>
          </div>
        </div>

        <!-- Action result -->
        <div v-if="actionError" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
          {{ actionError }}
        </div>
        <div v-if="actionSuccess" class="flex items-center gap-2 rounded-card border border-success/25 bg-success-soft px-4 py-3 text-sm text-success">
          <IconCircleCheck class="h-4 w-4 shrink-0" />
          {{ actionSuccess }}
        </div>
      </div>
    </template>

    <!-- Confirm modal -->
    <BaseModal v-model:open="confirmModal.visible" size="sm">
      <template #header>
        <div class="flex items-center gap-3">
          <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-danger-soft">
            <IconAlertTriangle class="h-5 w-5 text-danger" />
          </div>
          <h3 class="text-base font-semibold text-ink-strong">Confirm {{ confirmModal.action === 'rebuild' ? 'Rebuild' : 'Upgrade' }}</h3>
        </div>
      </template>
      <p class="text-sm text-ink-muted">
        Are you sure you want to {{ confirmModal.action === 'rebuild' ? 'rebuild' : 'upgrade' }} this chunk store?
        This operation may take a long time and cannot be undone.
      </p>
      <template #footer>
        <BaseButton variant="secondary" :disabled="actionInProgress" @click="confirmModal.visible = false">
          Cancel
        </BaseButton>
        <BaseButton variant="danger" :loading="actionInProgress" :disabled="actionInProgress" @click="runAction">
          {{ actionInProgress ? 'Running…' : 'Confirm' }}
        </BaseButton>
      </template>
    </BaseModal>
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
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseBadge, BaseButton, BaseCard, BaseModal } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const props = defineProps<{ storeId: string }>()
defineEmits<{ (e: 'back'): void }>()

const toast = useToast()

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
    toast.success(actionSuccess.value)
  } catch (e: any) {
    actionError.value = e.message || `Failed to ${confirmModal.action} chunk store.`
    toast.error(actionError.value || `Failed to ${confirmModal.action} chunk store.`)
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
