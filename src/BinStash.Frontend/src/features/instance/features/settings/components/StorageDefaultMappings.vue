<template>
  <div class="space-y-6">
    <!-- Header -->
    <div>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">Storage Class Default Mappings</h2>
      <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
        Define the default 1-to-1 mapping from each storage class to a chunk store.
        These mappings are copied to each tenant when it is created.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <Spinner />
      <span>Loading mappings…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-4 text-sm text-rose-700 dark:text-rose-400 flex items-center gap-2">
      <IconAlertCircle class="w-4 h-4 shrink-0" />
      {{ error }}
    </div>

    <template v-else>
      <!-- Prerequisites warning -->
      <div v-if="storageClasses.length === 0" class="bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-xl p-4 text-sm text-amber-700 dark:text-amber-400 flex items-center gap-2">
        <IconAlertCircle class="w-4 h-4 shrink-0" />
        No storage classes found. Create storage classes first before configuring mappings.
      </div>
      <div v-else-if="chunkStores.length === 0" class="bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-xl p-4 text-sm text-amber-700 dark:text-amber-400 flex items-center gap-2">
        <IconAlertCircle class="w-4 h-4 shrink-0" />
        No chunk stores found. Create at least one chunk store before configuring mappings.
      </div>
      <div v-else-if="chunkStores.length < storageClasses.length" class="bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-xl p-4 text-sm text-amber-700 dark:text-amber-400 flex items-center gap-2">
        <IconAlertCircle class="w-4 h-4 shrink-0" />
        A unique 1:1 mapping requires at least as many chunk stores ({{ chunkStores.length }}) as storage classes ({{ storageClasses.length }}).
      </div>

      <!-- Mapping editor -->
      <div v-else class="space-y-4">
        <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl overflow-hidden">
          <div class="px-5 py-3 border-b border-gray-100 dark:border-gray-700/60 grid grid-cols-12 gap-4 text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide">
            <div class="col-span-3">Storage Class</div>
            <div class="col-span-5">Chunk Store</div>
            <div class="col-span-2 text-center">Default</div>
            <div class="col-span-2 text-center">Enabled</div>
          </div>
          <div class="divide-y divide-gray-100 dark:divide-gray-700/60">
            <div
              v-for="(mapping, idx) in mappings"
              :key="mapping.storageClassName"
              class="px-5 py-3 grid grid-cols-12 gap-4 items-center"
            >
              <!-- Storage class name -->
              <div class="col-span-3">
                <span class="font-mono text-xs font-medium text-violet-600 dark:text-violet-400">{{ mapping.storageClassName }}</span>
              </div>

              <!-- Chunk store selector -->
              <div class="col-span-5">
                <select
                  v-model="mapping.chunkStoreId"
                  @change="onChunkStoreChange(idx)"
                  class="form-select w-full text-sm"
                  :disabled="saving"
                >
                  <option value="" disabled>Select chunk store…</option>
                  <option
                    v-for="cs in getChunkStoreOptions(idx)"
                    :key="cs.id"
                    :value="cs.id"
                  >
                    {{ cs.name }}
                  </option>
                </select>
              </div>

              <!-- Default radio -->
              <div class="col-span-2 flex justify-center">
                <input
                  type="radio"
                  name="default-mapping"
                  :checked="mapping.isDefault"
                  @change="setDefault(idx)"
                  :disabled="saving"
                  class="w-4 h-4 accent-violet-500"
                />
              </div>

              <!-- Enabled toggle -->
              <div class="col-span-2 flex justify-center">
                <label class="relative inline-flex items-center cursor-pointer">
                  <input type="checkbox" v-model="mapping.isEnabled" :disabled="saving" class="sr-only peer" />
                  <div class="w-9 h-5 bg-gray-200 peer-focus:outline-none rounded-full peer dark:bg-gray-700 peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-0.5 after:left-0.5 after:bg-white after:border-gray-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all dark:border-gray-600 peer-checked:bg-violet-500"></div>
                </label>
              </div>
            </div>
          </div>
        </div>

        <!-- Validation notice -->
        <div v-if="!canSave" class="text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1.5">
          <IconAlertCircle class="w-3.5 h-3.5 shrink-0" />
          Each storage class must map to a unique chunk store, with exactly one default.
        </div>

        <!-- Save button -->
        <div class="flex items-center gap-3">
          <button
            @click="saveMappings"
            :disabled="saving || !canSave"
            class="btn bg-violet-500 hover:bg-violet-600 text-white px-5 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2"
          >
            <svg v-if="saving" class="animate-spin w-4 h-4" viewBox="0 0 24 24" fill="none">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
            </svg>
            {{ saving ? 'Saving…' : 'Save Mappings' }}
          </button>
          <button
            @click="load"
            :disabled="saving"
            class="btn bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-600 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            Reset
          </button>
        </div>

        <!-- Save error -->
        <div v-if="saveError" class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-3 text-sm text-rose-700 dark:text-rose-400">
          {{ saveError }}
        </div>
      </div>
    </template>

    <!-- Success toast -->
    <div
      v-if="successMsg"
      class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg flex items-center gap-2"
    >
      <IconCircleCheck class="w-4 h-4 shrink-0" />
      {{ successMsg }}
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue'
import { IconAlertCircle, IconCircleCheck } from '@tabler/icons-vue'
import {
  listStorageClasses,
  listStorageDefaultMappings,
  saveStorageDefaultMappings,
  type StorageClassDto,
  type StorageClassDefaultMappingDto,
} from '@/api/storageClasses'
import { listChunkStores, type ChunkStoreSummaryDto } from '@/api/chunkStores'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const successMsg = ref<string | null>(null)

const storageClasses = ref<StorageClassDto[]>([])
const chunkStores = ref<ChunkStoreSummaryDto[]>([])
const mappings = ref<StorageClassDefaultMappingDto[]>([])

async function load() {
  loading.value = true
  error.value = null
  try {
    const [classes, stores, existing] = await Promise.all([
      listStorageClasses(),
      listChunkStores(),
      listStorageDefaultMappings(),
    ])
    storageClasses.value = classes
    chunkStores.value = stores
    buildMappings(classes, stores, existing)
  } catch (e: any) {
    error.value = e.message || 'Failed to load mapping data.'
  } finally {
    loading.value = false
  }
}

function buildMappings(
  classes: StorageClassDto[],
  stores: ChunkStoreSummaryDto[],
  existing: StorageClassDefaultMappingDto[]
) {
  const existingByClass = new Map(existing.map((m) => [m.storageClassName, m]))
  const usedIds = new Set<string>()
  const result: StorageClassDefaultMappingDto[] = []

  for (const sc of classes) {
    const ex = existingByClass.get(sc.name)
    let csId = ex?.chunkStoreId && stores.some((s) => s.id === ex.chunkStoreId) ? ex.chunkStoreId : ''
    if (csId && usedIds.has(csId)) csId = ''
    if (!csId) {
      const avail = stores.find((s) => !usedIds.has(s.id))
      csId = avail?.id ?? ''
    }
    if (csId) usedIds.add(csId)
    result.push({
      storageClassName: sc.name,
      chunkStoreId: csId,
      isDefault: Boolean(ex?.isDefault),
      isEnabled: ex?.isEnabled ?? true,
    })
  }

  // Ensure exactly one default
  const defaults = result.filter((m) => m.isDefault)
  if (defaults.length === 0 && result.length > 0) result[0].isDefault = true
  else if (defaults.length > 1) result.forEach((m, i) => (m.isDefault = i === 0))

  mappings.value = result
}

function getChunkStoreOptions(index: number): ChunkStoreSummaryDto[] {
  const usedByOthers = new Set(
    mappings.value.filter((_, i) => i !== index).map((m) => m.chunkStoreId).filter(Boolean)
  )
  const current = mappings.value[index]?.chunkStoreId
  return chunkStores.value.filter((cs) => cs.id === current || !usedByOthers.has(cs.id))
}

function onChunkStoreChange(_idx: number) {
  // Re-validate uniqueness
  const used = new Set<string>()
  for (const m of mappings.value) {
    if (m.chunkStoreId && !used.has(m.chunkStoreId)) {
      used.add(m.chunkStoreId)
    } else {
      m.chunkStoreId = ''
    }
  }
}

function setDefault(index: number) {
  mappings.value.forEach((m, i) => (m.isDefault = i === index))
}

const canSave = computed(() => {
  if (!storageClasses.value.length || !chunkStores.value.length) return false
  if (mappings.value.some((m) => !m.chunkStoreId)) return false
  const ids = mappings.value.map((m) => m.chunkStoreId)
  if (new Set(ids).size !== ids.length) return false
  if (mappings.value.filter((m) => m.isDefault).length !== 1) return false
  return true
})

async function saveMappings() {
  saveError.value = null
  saving.value = true
  try {
    await saveStorageDefaultMappings(mappings.value)
    showSuccess('Default mappings saved successfully.')
  } catch (e: any) {
    saveError.value = e.message || 'Failed to save mappings.'
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