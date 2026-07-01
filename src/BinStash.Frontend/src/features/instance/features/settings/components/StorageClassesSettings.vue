<template>
  <div class="space-y-6">
    <!-- Header + Add button -->
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-lg font-semibold text-ink-strong">Storage Classes</h2>
        <p class="mt-0.5 text-sm text-ink-muted">
          Define named storage tiers that repositories can be assigned to.
        </p>
      </div>
      <BaseButton :icon="IconPlus" @click="showAddForm = true">
        Add Storage Class
      </BaseButton>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading storage classes…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      {{ error }}
    </div>

    <!-- Empty state -->
    <BaseCard v-else-if="storageClasses.length === 0 && !showAddForm">
      <EmptyState
        :icon="IconStack"
        title="No storage classes yet"
        description="Add a storage class to get started."
      />
    </BaseCard>

    <!-- Add form -->
    <BaseCard v-if="showAddForm" accent>
      <template #header>
        <h3 class="font-semibold text-ink-strong">New Storage Class</h3>
      </template>
      <div class="space-y-4">
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <BaseInput
            v-model="newClass.name"
            label="Name"
            required
            placeholder="e.g. standard"
            :error="addError && !newClass.name ? 'Required' : undefined"
          />
          <BaseInput
            v-model="newClass.displayName"
            label="Display Name"
            required
            placeholder="e.g. Standard"
            :error="addError && !newClass.displayName ? 'Required' : undefined"
          />
          <BaseInput
            v-model="newClass.description"
            label="Description"
            placeholder="Optional description"
          />
        </div>
        <div v-if="addError" class="text-xs text-danger">{{ addError }}</div>
        <div class="flex items-center gap-3">
          <BaseButton :loading="saving" :disabled="saving" @click="saveNewClass">
            {{ saving ? 'Saving…' : 'Save' }}
          </BaseButton>
          <BaseButton variant="secondary" @click="cancelAdd">Cancel</BaseButton>
        </div>
      </div>
    </BaseCard>

    <!-- Storage class list -->
    <BaseCard v-if="!loading && storageClasses.length > 0" :padded="false">
      <DataTable :columns="columns" :items="storageClasses" :hover="false" row-key="name">
        <template #cell-name="{ item }">
          <span class="font-mono text-xs font-medium text-accent">{{ item.name }}</span>
        </template>
        <template #cell-displayName="{ item }">
          <span class="font-medium text-ink-strong">{{ item.displayName }}</span>
        </template>
        <template #cell-description="{ item }">
          {{ item.description || '—' }}
        </template>
      </DataTable>
    </BaseCard>
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted } from 'vue'
import { IconPlus, IconStack } from '@tabler/icons-vue'
import {
  listStorageClasses,
  createStorageClasses,
  type StorageClassDto,
} from '@/api/storageClasses'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseCard, BaseInput, DataTable, EmptyState, type Column } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const addError = ref<string | null>(null)
const showAddForm = ref(false)

const storageClasses = ref<StorageClassDto[]>([])

const newClass = ref<StorageClassDto>({ name: '', displayName: '', description: '', isDeprecated: false })

const columns: Column[] = [
  { key: 'name', label: 'Name' },
  { key: 'displayName', label: 'Display Name' },
  { key: 'description', label: 'Description' },
]

async function load() {
  loading.value = true
  error.value = null
  try {
    storageClasses.value = await listStorageClasses()
  } catch (e: any) {
    error.value = e.message || 'Failed to load storage classes.'
  } finally {
    loading.value = false
  }
}

function cancelAdd() {
  showAddForm.value = false
  addError.value = null
  newClass.value = { name: '', displayName: '', description: '', isDeprecated: false }
}

async function saveNewClass() {
  addError.value = null
  if (!newClass.value.name.trim()) {
    addError.value = 'Name is required.'
    return
  }
  if (!newClass.value.displayName.trim()) {
    addError.value = 'Display Name is required.'
    return
  }
  saving.value = true
  try {
    // The API adds to existing classes, so we send all current + new
    const allClasses = [
      ...storageClasses.value,
      { ...newClass.value },
    ]
    await createStorageClasses(allClasses)
    await load()
    cancelAdd()
    toast.success('Storage class created successfully.')
  } catch (e: any) {
    addError.value = e.message || 'Failed to create storage class.'
    toast.error(addError.value || 'Failed to create storage class.')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
