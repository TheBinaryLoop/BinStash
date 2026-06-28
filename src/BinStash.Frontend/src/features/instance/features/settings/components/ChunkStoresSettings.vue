<template>
  <div class="space-y-6">
    <!-- Detail view when a store is selected -->
    <ChunkStoreDetail
      v-if="selectedStoreId"
      :storeId="selectedStoreId"
      @back="selectedStoreId = null"
    />

    <!-- List view -->
    <template v-else>
      <!-- Header + Add button -->
      <div class="flex items-center justify-between">
        <div>
          <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Chunk Stores</h2>
          <p class="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
            Manage the underlying storage backends that hold deduplicated chunk data.
          </p>
        </div>
        <button
          @click="showAddForm = true"
          class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff]"
        >
          <IconPlus class="w-4 h-4" />
          Add Chunk Store
        </button>
      </div>

      <!-- Loading -->
      <div v-if="loading" class="flex items-center gap-3 text-slate-500 dark:text-slate-400 py-8 justify-center">
        <Spinner />
        <span>Loading chunk stores…</span>
      </div>

      <!-- Error -->
      <div v-else-if="error" class="rounded-[28px] border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700 dark:border-rose-500/20 dark:bg-rose-500/10 dark:text-rose-400">
        {{ error }}
      </div>

      <template v-else>
        <!-- Add form -->
        <div v-if="showAddForm" class="rounded-[28px] border border-[#7C86FF]/30 bg-white p-5 shadow-sm dark:border-[#7C86FF]/20 dark:bg-[#0F172D]">
          <h3 class="font-semibold text-slate-900 dark:text-white mb-4">New Chunk Store</h3>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Name <span class="text-rose-500">*</span></label>
              <input
                v-model="newStore.name"
                type="text"
                placeholder="e.g. primary-store"
                class="form-input w-full text-sm"
              />
            </div>
            <div>
              <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Type <span class="text-rose-500">*</span></label>
              <select v-model.number="newStore.typeValue" class="form-select w-full text-sm">
                <option :value="-1" disabled>— Select a type —</option>
                <option v-for="t in chunkStoreTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
              </select>
            </div>
            <div class="sm:col-span-2">
              <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Local Path <span class="text-rose-500">*</span></label>
              <input
                v-model="newStore.localPath"
                type="text"
                placeholder="e.g. /data/chunks/primary"
                class="form-input w-full text-sm"
              />
            </div>
          </div>

          <!-- Chunker (optional) -->
          <div class="mt-4">
            <button
              type="button"
              @click="showChunkerConfig = !showChunkerConfig"
              class="text-xs font-medium text-[#7C86FF] hover:underline flex items-center gap-1"
            >
              <IconChevronDown class="w-3.5 h-3.5 transition" :class="showChunkerConfig ? 'rotate-180' : ''" />
              {{ showChunkerConfig ? 'Hide' : 'Show' }} Chunker Configuration (optional)
            </button>
            <div v-if="showChunkerConfig" class="mt-3 grid grid-cols-1 sm:grid-cols-2 gap-4 p-4 bg-slate-50 dark:bg-white/[0.03] rounded-2xl">
              <div>
                <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Chunker Type</label>
                <input v-model="newChunker.type" type="text" placeholder="e.g. cdc" class="form-input w-full text-sm" />
              </div>
              <div>
                <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Min Chunk Size (bytes)</label>
                <input v-model.number="newChunker.minChunkSize" type="number" placeholder="e.g. 65536" class="form-input w-full text-sm" />
              </div>
              <div>
                <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Avg Chunk Size (bytes)</label>
                <input v-model.number="newChunker.avgChunkSize" type="number" placeholder="e.g. 262144" class="form-input w-full text-sm" />
              </div>
              <div>
                <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Max Chunk Size (bytes)</label>
                <input v-model.number="newChunker.maxChunkSize" type="number" placeholder="e.g. 1048576" class="form-input w-full text-sm" />
              </div>
            </div>
          </div>

          <div v-if="addError" class="mt-3 text-xs text-rose-600 dark:text-rose-400">{{ addError }}</div>

          <div class="flex items-center gap-3 mt-4">
            <button
              @click="saveNewStore"
              :disabled="saving"
              class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] disabled:opacity-60"
            >
              <Spinner v-if="saving" class="w-4 h-4" />
              {{ saving ? 'Saving…' : 'Save' }}
            </button>
            <button
              @click="cancelAdd"
              class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2.5 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-white/[0.03] transition"
            >
              Cancel
            </button>
          </div>
        </div>

        <!-- Empty state -->
        <div
          v-if="stores.length === 0 && !showAddForm"
          class="rounded-[28px] border border-dashed border-slate-300 bg-white p-10 text-center shadow-sm dark:border-white/10 dark:bg-[#0F172D]"
        >
          <div class="w-12 h-12 rounded-full bg-slate-100 dark:bg-white/5 flex items-center justify-center mx-auto mb-3">
            <IconDatabase class="text-slate-400 w-6 h-6" />
          </div>
          <p class="text-slate-600 dark:text-slate-400 font-medium">No chunk stores yet</p>
          <p class="text-sm text-slate-500 dark:text-slate-500 mt-1">Add a chunk store to get started.</p>
        </div>

        <!-- Stores list -->
        <div v-if="stores.length > 0" class="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-xs font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wide bg-slate-50 dark:bg-white/[0.03] border-b border-slate-200 dark:border-white/5">
                <th class="px-5 py-3 text-left">Name</th>
                <th class="px-5 py-3 text-left">ID</th>
                <th class="px-5 py-3 text-right"></th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-100 dark:divide-white/5">
              <tr
                v-for="store in stores"
                :key="store.id"
                class="hover:bg-slate-50 dark:hover:bg-white/[0.03] transition cursor-pointer"
                @click="selectedStoreId = store.id"
              >
                <td class="px-5 py-3 font-medium text-slate-900 dark:text-white">
                  {{ store.name }}
                </td>
                <td class="px-5 py-3 font-mono text-xs text-slate-400 dark:text-slate-500">
                  {{ store.id }}
                </td>
                <td class="px-5 py-3 text-right">
                  <span class="text-xs text-[#7C86FF] hover:text-[#6974ff] font-medium">
                    View →
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <!-- Success toast -->
      <div
        v-if="successMsg"
        class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg flex items-center gap-2"
      >
        <IconCircleCheck color="white" class="w-4 h-4 shrink-0" />
        {{ successMsg }}
      </div>
    </template>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, onMounted } from 'vue'
import { IconPlus, IconDatabase, IconCircleCheck, IconChevronDown } from '@tabler/icons-vue'
import {
  listChunkStores,
  createChunkStore,
  getEnabledChunkStoreTypes,
  type ChunkStoreSummaryDto,
  type ChunkStoreChunkerDto,
} from '@/api/chunkStores'
import ChunkStoreDetail from './ChunkStoreDetail.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const addError = ref<string | null>(null)
const successMsg = ref<string | null>(null)
const showAddForm = ref(false)
const showChunkerConfig = ref(false)
const selectedStoreId = ref<string | null>(null)

const stores = ref<ChunkStoreSummaryDto[]>([])
const chunkStoreTypes = ref<{ name: string; value: number }[]>([])

const newStore = reactive({ name: '', typeValue: -1, localPath: '' })
const newChunker = reactive<Partial<ChunkStoreChunkerDto>>({ type: '', minChunkSize: null, avgChunkSize: null, maxChunkSize: null })

async function load() {
  loading.value = true
  error.value = null
  try {
    const [storeList, types] = await Promise.all([
      listChunkStores(),
      getEnabledChunkStoreTypes(),
    ])
    stores.value = storeList
    chunkStoreTypes.value = types
    if (types.length > 0 && newStore.typeValue === -1) {
      newStore.typeValue = types[0].value
    }
  } catch (e: any) {
    error.value = e.message || 'Failed to load chunk stores.'
  } finally {
    loading.value = false
  }
}

function cancelAdd() {
  showAddForm.value = false
  showChunkerConfig.value = false
  addError.value = null
  newStore.name = ''
  newStore.typeValue = chunkStoreTypes.value.length > 0 ? chunkStoreTypes.value[0].value : -1
  newStore.localPath = ''
  newChunker.type = ''
  newChunker.minChunkSize = null
  newChunker.avgChunkSize = null
  newChunker.maxChunkSize = null
}

async function saveNewStore() {
  addError.value = null
  if (!newStore.name.trim()) { addError.value = 'Name is required.'; return }
  if (newStore.typeValue === -1) { addError.value = 'Type is required.'; return }
  if (!newStore.localPath.trim()) { addError.value = 'Local path is required.'; return }

  const hasChunker = showChunkerConfig.value && newChunker.type?.trim()
  saving.value = true
  try {
    await createChunkStore({
      name: newStore.name.trim(),
      type: newStore.typeValue.toString(),
      localPath: newStore.localPath.trim(),
      chunker: hasChunker
        ? {
            type: newChunker.type!,
            minChunkSize: newChunker.minChunkSize ?? null,
            avgChunkSize: newChunker.avgChunkSize ?? null,
            maxChunkSize: newChunker.maxChunkSize ?? null,
          }
        : null,
    })
    await load()
    cancelAdd()
    showSuccess('Chunk store created successfully.')
  } catch (e: any) {
    addError.value = e.message || 'Failed to create chunk store.'
  } finally {
    saving.value = false
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

onMounted(load)
</script>