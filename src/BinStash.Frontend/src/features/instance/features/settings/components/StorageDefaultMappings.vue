<template>
  <div class="space-y-6">
    <!-- Header -->
    <div>
      <h2 class="text-lg font-semibold text-ink-strong">Storage Class Default Mappings</h2>
      <p class="mt-0.5 text-sm text-ink-muted">
        Define the default 1-to-1 mapping from each storage class to a chunk store.
        These mappings are copied to each tenant when it is created.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading mappings…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="flex items-center gap-2 rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      <IconAlertCircle class="h-4 w-4 shrink-0" />
      {{ error }}
    </div>

    <template v-else>
      <!-- Prerequisites warning -->
      <div v-if="storageClasses.length === 0" class="flex items-center gap-2 rounded-card border border-warning/25 bg-warning-soft px-4 py-3 text-sm text-warning">
        <IconAlertCircle class="h-4 w-4 shrink-0" />
        No storage classes found. Create storage classes first before configuring mappings.
      </div>
      <div v-else-if="chunkStores.length === 0" class="flex items-center gap-2 rounded-card border border-warning/25 bg-warning-soft px-4 py-3 text-sm text-warning">
        <IconAlertCircle class="h-4 w-4 shrink-0" />
        No chunk stores found. Create at least one chunk store before configuring mappings.
      </div>
      <div v-else-if="chunkStores.length < storageClasses.length" class="flex items-center gap-2 rounded-card border border-warning/25 bg-warning-soft px-4 py-3 text-sm text-warning">
        <IconAlertCircle class="h-4 w-4 shrink-0" />
        A unique 1:1 mapping requires at least as many chunk stores ({{ chunkStores.length }}) as storage classes ({{ storageClasses.length }}).
      </div>

      <!-- Mapping editor -->
      <div v-else class="space-y-4">
        <div class="overflow-hidden rounded-card border border-hairline bg-card">
          <div class="grid grid-cols-12 gap-4 border-b border-hairline px-5 py-3 text-xs font-semibold uppercase tracking-wide text-ink-subtle">
            <div class="col-span-3">Storage Class</div>
            <div class="col-span-5">Chunk Store</div>
            <div class="col-span-2 text-center">Default</div>
            <div class="col-span-2 text-center">Enabled</div>
          </div>
          <div class="divide-y divide-hairline">
            <div
              v-for="(mapping, idx) in mappings"
              :key="mapping.storageClassName"
              class="grid grid-cols-12 items-center gap-4 px-5 py-3"
            >
              <!-- Storage class name -->
              <div class="col-span-3">
                <span class="font-mono text-xs font-medium text-accent">{{ mapping.storageClassName }}</span>
              </div>

              <!-- Chunk store selector -->
              <div class="col-span-5">
                <BaseSelect
                  v-model="mapping.chunkStoreId"
                  :disabled="saving"
                  @change="onChunkStoreChange(idx)"
                >
                  <option value="" disabled>Select chunk store…</option>
                  <option
                    v-for="cs in getChunkStoreOptions(idx)"
                    :key="cs.id"
                    :value="cs.id"
                  >
                    {{ cs.name }}
                  </option>
                </BaseSelect>
              </div>

              <!-- Default radio -->
              <div class="col-span-2 flex justify-center">
                <input
                  type="radio"
                  name="default-mapping"
                  :checked="mapping.isDefault"
                  @change="setDefault(idx)"
                  :disabled="saving"
                  class="h-4 w-4 accent-[var(--color-accent)]"
                />
              </div>

              <!-- Enabled toggle -->
              <div class="col-span-2 flex justify-center">
                <BaseSwitch v-model="mapping.isEnabled" :disabled="saving" />
              </div>
            </div>
          </div>
        </div>

        <!-- Validation notice -->
        <div v-if="!canSave" class="flex items-center gap-1.5 text-xs text-warning">
          <IconAlertCircle class="h-3.5 w-3.5 shrink-0" />
          Each storage class must map to a unique chunk store, with exactly one default.
        </div>

        <!-- Save button -->
        <div class="flex items-center gap-3">
          <BaseButton :loading="saving" :disabled="saving || !canSave" @click="saveMappings">
            {{ saving ? 'Saving…' : 'Save Mappings' }}
          </BaseButton>
          <BaseButton variant="secondary" :disabled="saving" @click="load">
            Reset
          </BaseButton>
        </div>

        <!-- Save error -->
        <div v-if="saveError" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
          {{ saveError }}
        </div>
      </div>
    </template>
  </div>
</template>

<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue'
import { IconAlertCircle } from '@tabler/icons-vue'
import {
  listStorageClasses,
  listStorageDefaultMappings,
  saveStorageDefaultMappings,
  type StorageClassDto,
  type StorageClassDefaultMappingDto,
} from '@/api/storageClasses'
import { listChunkStores, type ChunkStoreSummaryDto } from '@/api/chunkStores'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseSelect, BaseSwitch } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)

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
    toast.success('Default mappings saved successfully.')
  } catch (e: any) {
    saveError.value = e.message || 'Failed to save mappings.'
    toast.error(saveError.value || 'Failed to save mappings.')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
