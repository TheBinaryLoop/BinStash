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
          <h2 class="text-lg font-semibold text-ink-strong">Chunk Stores</h2>
          <p class="mt-0.5 text-sm text-ink-muted">
            Manage the underlying storage backends that hold deduplicated chunk data.
          </p>
        </div>
        <BaseButton :icon="IconPlus" @click="showAddForm = true">
          Add Chunk Store
        </BaseButton>
      </div>

      <!-- Loading -->
      <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
        <Spinner :size="20" color="var(--color-accent)" />
        <span>Loading chunk stores…</span>
      </div>

      <!-- Error -->
      <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
        {{ error }}
      </div>

      <template v-else>
        <!-- Add form -->
        <BaseCard v-if="showAddForm" accent>
          <template #header>
            <h3 class="font-semibold text-ink-strong">New Chunk Store</h3>
          </template>
          <div class="space-y-4">
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <BaseInput
                v-model="newStore.name"
                label="Name"
                required
                placeholder="e.g. primary-store"
              />
              <BaseSelect
                v-model.number="newStore.typeValue"
                label="Type"
                required
              >
                <option :value="-1" disabled>— Select a type —</option>
                <option v-for="t in chunkStoreTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
              </BaseSelect>
              <div class="sm:col-span-2">
                <BaseInput
                  v-model="newStore.localPath"
                  label="Local Path"
                  required
                  placeholder="e.g. /data/chunks/primary"
                />
              </div>
            </div>

            <!-- Chunker (optional) -->
            <div>
              <button
                type="button"
                @click="showChunkerConfig = !showChunkerConfig"
                class="flex items-center gap-1 text-xs font-medium text-accent hover:underline"
              >
                <IconChevronDown class="h-3.5 w-3.5 transition" :class="showChunkerConfig ? 'rotate-180' : ''" />
                {{ showChunkerConfig ? 'Hide' : 'Show' }} Chunker Configuration (optional)
              </button>
              <div v-if="showChunkerConfig" class="mt-3 grid grid-cols-1 gap-4 rounded-card bg-raised p-4 sm:grid-cols-2">
                <BaseInput v-model="newChunker.type" label="Chunker Type" placeholder="e.g. cdc" />
                <BaseInput v-model.number="newChunker.minChunkSize" type="number" label="Min Chunk Size (bytes)" placeholder="e.g. 65536" />
                <BaseInput v-model.number="newChunker.avgChunkSize" type="number" label="Avg Chunk Size (bytes)" placeholder="e.g. 262144" />
                <BaseInput v-model.number="newChunker.maxChunkSize" type="number" label="Max Chunk Size (bytes)" placeholder="e.g. 1048576" />
              </div>
            </div>

            <div v-if="addError" class="text-xs text-danger">{{ addError }}</div>

            <div class="flex items-center gap-3">
              <BaseButton :loading="saving" :disabled="saving" @click="saveNewStore">
                {{ saving ? 'Saving…' : 'Save' }}
              </BaseButton>
              <BaseButton variant="secondary" @click="cancelAdd">Cancel</BaseButton>
            </div>
          </div>
        </BaseCard>

        <!-- Empty state -->
        <BaseCard v-if="stores.length === 0 && !showAddForm">
          <EmptyState
            :icon="IconDatabase"
            title="No chunk stores yet"
            description="Add a chunk store to get started."
          />
        </BaseCard>

        <!-- Stores list -->
        <BaseCard v-if="stores.length > 0" :padded="false">
          <DataTable
            :columns="columns"
            :items="stores"
            :on-row-click="(store) => (selectedStoreId = store.id)"
          >
            <template #cell-name="{ item }">
              <span class="font-medium text-ink-strong">{{ item.name }}</span>
            </template>
            <template #cell-id="{ item }">
              <span class="font-mono text-xs text-ink-subtle">{{ item.id }}</span>
            </template>
            <template #cell-actions>
              <span class="text-xs font-medium text-accent">View →</span>
            </template>
          </DataTable>
        </BaseCard>
      </template>
    </template>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, onMounted } from 'vue'
import { IconPlus, IconDatabase, IconChevronDown } from '@tabler/icons-vue'
import {
  listChunkStores,
  createChunkStore,
  getEnabledChunkStoreTypes,
  type ChunkStoreSummaryDto,
  type ChunkStoreChunkerDto,
} from '@/api/chunkStores'
import ChunkStoreDetail from './ChunkStoreDetail.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseCard, BaseInput, BaseSelect, DataTable, EmptyState, type Column } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const addError = ref<string | null>(null)
const showAddForm = ref(false)
const showChunkerConfig = ref(false)
const selectedStoreId = ref<string | null>(null)

const stores = ref<ChunkStoreSummaryDto[]>([])
const chunkStoreTypes = ref<{ name: string; value: number }[]>([])

const newStore = reactive({ name: '', typeValue: -1, localPath: '' })
const newChunker = reactive<Partial<ChunkStoreChunkerDto>>({ type: '', minChunkSize: null, avgChunkSize: null, maxChunkSize: null })

const columns: Column[] = [
  { key: 'name', label: 'Name' },
  { key: 'id', label: 'ID' },
  { key: 'actions', label: '', align: 'right' },
]

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
    toast.success('Chunk store created successfully.')
  } catch (e: any) {
    addError.value = e.message || 'Failed to create chunk store.'
    toast.error(addError.value || 'Failed to create chunk store.')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
