<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4 w-full max-w-none">
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 5: Create Storage Defaults</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Map each storage class to a unique chunk store, then choose one default mapping and set enabled state.
    </p>

    <div v-if="loadingInitial" class="text-gray-500 dark:text-gray-400 text-sm">Loading setup data...</div>

    <template v-else>
      <div v-if="!storageClasses.length" class="text-red-600 dark:text-red-400 text-sm">
        No storage classes found. Create storage classes first.
      </div>
      <div v-else-if="!chunkStores.length" class="text-red-600 dark:text-red-400 text-sm">
        No chunk stores found. Create at least one chunk store first.
      </div>
      <div v-else-if="chunkStores.length < storageClasses.length" class="text-red-600 dark:text-red-400 text-sm">
        A unique 1:1 mapping requires at least as many chunk stores as storage classes.
      </div>
      <div v-else class="flex flex-col gap-3 w-full">
        <div
          v-for="(mapping, idx) in mappings"
          :key="mapping.storageClassName"
          class="flex flex-row flex-wrap gap-4 items-center bg-gray-50 dark:bg-gray-700/50 border border-gray-200 dark:border-gray-600 rounded-lg px-4 py-3 w-full box-border"
        >
          <div class="min-w-32">
            <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Storage Class</label>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-100">{{ mapping.storageClassName }}</div>
          </div>

          <div class="flex-1 min-w-48">
            <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Chunk Store</label>
            <select
              v-model="mapping.chunkStoreId"
              @change="onChunkStoreChange(idx)"
              class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
              :disabled="loading"
              required
            >
              <option value="" disabled>Select chunk store</option>
              <option
                v-for="cs in getChunkStoreOptions(idx)"
                :key="cs.id"
                :value="cs.id"
              >
                {{ cs.name }}
              </option>
            </select>
          </div>

          <div class="min-w-24">
            <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Default</label>
            <label class="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="default-storage-mapping"
                :checked="mapping.isDefault"
                @change="setDefault(idx)"
                :disabled="loading"
                class="accent-violet-500"
              />
              <span class="text-sm text-gray-700 dark:text-gray-300">Default</span>
            </label>
          </div>

          <div class="min-w-24">
            <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Enabled</label>
            <label class="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                v-model="mapping.isEnabled"
                :disabled="loading"
                class="accent-violet-500"
              />
              <span class="text-sm text-gray-700 dark:text-gray-300">{{ mapping.isEnabled ? 'True' : 'False' }}</span>
            </label>
          </div>
        </div>
      </div>
    </template>

    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
    <div v-if="success" class="text-green-600 dark:text-green-400 text-sm">
      Storage defaults saved successfully.
    </div>

<button
      type="submit"
      :disabled="loading"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Saving...' : 'Save Storage Defaults' }}
    </button>
  </form>
</template>

<script lang="ts" setup>
import { computed, onMounted, ref } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { createStorageDefaults } from '@/features/setup/api/setup.api'
import Spinner from '@/shared/components/feedback/Spinner.vue'

interface MappingFormModel {
  storageClassName: string
  chunkStoreId: string
  isDefault: boolean
  isEnabled: boolean
}

const setupStore = useSetupStore()
const loading = ref(false)
const loadingInitial = ref(false)
const error = ref<string | null>(null)
const success = ref(false)
const mappings = ref<MappingFormModel[]>([])

const status = computed(() => setupStore.status)
const storageClasses = computed(() => status.value?.data?.storageClasses ?? [])
const chunkStores = computed(() => status.value?.data?.chunkStores ?? [])
const existingMappings = computed(() => status.value?.data?.storageClassDefaultMappings ?? [])

function initializeMappings() {
  const existingByStorageClass = new Map(
    existingMappings.value.map(m => [m.storageClassName, m])
  )
  const usedChunkStoreIds = new Set<string>()
  const nextMappings: MappingFormModel[] = []

  for (const sc of storageClasses.value) {
    const existing = existingByStorageClass.get(sc.name)
    let selectedChunkStoreId =
      existing?.chunkStoreId && chunkStores.value.some(cs => cs.id === existing.chunkStoreId)
        ? existing.chunkStoreId
        : ''

    if (selectedChunkStoreId && usedChunkStoreIds.has(selectedChunkStoreId)) {
      selectedChunkStoreId = ''
    }

    if (!selectedChunkStoreId) {
      const available = chunkStores.value.find(cs => !usedChunkStoreIds.has(cs.id))
      selectedChunkStoreId = available?.id ?? ''
    }

    if (selectedChunkStoreId) {
      usedChunkStoreIds.add(selectedChunkStoreId)
    }

    nextMappings.push({
      storageClassName: sc.name,
      chunkStoreId: selectedChunkStoreId,
      isDefault: Boolean(existing?.isDefault),
      isEnabled: existing?.isEnabled ?? true,
    })
  }

  const defaultIndexes = nextMappings
    .map((m, i) => (m.isDefault ? i : -1))
    .filter(i => i >= 0)

  if (defaultIndexes.length === 0 && nextMappings.length > 0) {
    nextMappings[0].isDefault = true
  } else if (defaultIndexes.length > 1) {
    const keep = defaultIndexes[0]
    nextMappings.forEach((m, i) => {
      m.isDefault = i === keep
    })
  }

  mappings.value = nextMappings
  ensureUniqueMappings()
}

function ensureUniqueMappings() {
  const used = new Set<string>()

  for (const m of mappings.value) {
    if (m.chunkStoreId && !used.has(m.chunkStoreId)) {
      used.add(m.chunkStoreId)
      continue
    }

    const replacement = chunkStores.value.find(cs => !used.has(cs.id))
    m.chunkStoreId = replacement?.id ?? ''
    if (m.chunkStoreId) {
      used.add(m.chunkStoreId)
    }
  }
}

function getChunkStoreOptions(index: number) {
  const selectedByOthers = new Set(
    mappings.value
      .filter((_, i) => i !== index)
      .map(m => m.chunkStoreId)
      .filter(Boolean)
  )
  const current = mappings.value[index]?.chunkStoreId
  return chunkStores.value.filter(cs => cs.id === current || !selectedByOthers.has(cs.id))
}

function onChunkStoreChange(_index: number) {
  ensureUniqueMappings()
}

function setDefault(index: number) {
  mappings.value.forEach((m, i) => {
    m.isDefault = i === index
  })
}

const hasUniqueMappings = computed(() => {
  const ids = mappings.value.map(m => m.chunkStoreId).filter(Boolean)
  return ids.length === mappings.value.length && new Set(ids).size === ids.length
})

const hasOneDefault = computed(
  () => mappings.value.filter(m => m.isDefault).length === 1
)

const canSubmit = computed(() => {
  if (!storageClasses.value.length || !chunkStores.value.length) return false
  if (chunkStores.value.length < storageClasses.value.length) return false
  if (mappings.value.length !== storageClasses.value.length) return false
  if (mappings.value.some(m => !m.chunkStoreId)) return false
  if (!hasUniqueMappings.value) return false
  if (!hasOneDefault.value) return false
  return true
})

onMounted(async () => {
  if (!setupStore.status) {
    loadingInitial.value = true
    try {
      await setupStore.fetchStatus()
    } finally {
      loadingInitial.value = false
    }
  }
  initializeMappings()
})

async function onSubmit() {
  error.value = null
  success.value = false

  if (!canSubmit.value) {
    error.value =
      'Each storage class must map to a unique chunk store, with exactly one default mapping.'
    return
  }

  loading.value = true
  try {
    await createStorageDefaults(
      mappings.value.map(m => ({
        storageClassName: m.storageClassName,
        chunkStoreId: m.chunkStoreId,
        isDefault: m.isDefault,
        isEnabled: m.isEnabled,
      }))
    )
    success.value = true
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to save storage defaults.'
  } finally {
    loading.value = false
  }
}
</script>