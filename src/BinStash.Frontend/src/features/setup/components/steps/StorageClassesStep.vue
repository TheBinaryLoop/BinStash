<template>
  <form
    @submit.prevent="onSubmit"
    class="flex w-full max-w-none flex-col gap-4"
  >
    <h2 class="text-xl font-bold text-ink-strong">Step 4: Create Storage Classes</h2>
    <p class="text-sm text-ink-muted">
      Add one or more storage classes. At least one is required.
    </p>
    <div class="flex w-full max-w-full flex-col gap-3">
      <div
        v-for="(sc, idx) in storageClasses"
        :key="idx"
        class="box-border flex w-full flex-row flex-wrap items-end gap-4 rounded-card border border-hairline bg-raised px-4 py-3"
      >
        <BaseInput
          v-model="sc.name"
          label="Name"
          placeholder="Name"
          required
          class="min-w-40 flex-1"
        />
        <BaseInput
          v-model="sc.displayName"
          label="Display Name"
          placeholder="Display Name"
          required
          class="min-w-40 flex-1"
        />
        <BaseInput
          v-model="sc.description"
          label="Description"
          placeholder="Description"
          class="min-w-40 flex-1"
        />
        <BaseButton
          v-if="storageClasses.length > 1"
          type="button"
          variant="ghost"
          size="sm"
          class="flex-none text-danger"
          @click="remove(idx)"
        >
          Remove
        </BaseButton>
      </div>
    </div>
    <BaseButton type="button" variant="secondary" size="sm" class="w-fit" @click="add">
      Add Storage Class
    </BaseButton>
    <div
      v-if="error"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >{{ error }}</div>
    <BaseButton type="submit" :loading="loading" :disabled="loading">
      {{ loading ? 'Saving...' : 'Save Storage Classes' }}
    </BaseButton>
    <div
      v-if="success"
      class="rounded-card border border-success/25 bg-success-soft px-4 py-3 text-sm text-success"
    >
      Storage classes saved successfully.
    </div>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { createStorageClasses } from '@/features/setup/api/setup.api'
import { BaseInput, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const loading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

interface StorageClass {
  name: string
  displayName: string
  description?: string
}

const storageClasses = ref<StorageClass[]>([
  { name: '', displayName: '', description: '' }
])

function add() {
  storageClasses.value.push({ name: '', displayName: '', description: '' })
}

function remove(idx: number) {
  storageClasses.value.splice(idx, 1)
}

async function onSubmit() {
  error.value = null
  success.value = false
  if (storageClasses.value.length === 0) {
    error.value = 'At least one storage class is required.'
    return
  }
  if (storageClasses.value.some(sc => !sc.name || !sc.displayName)) {
    error.value = 'Name and Display Name are required for each storage class.'
    return
  }
  loading.value = true
  try {
    await createStorageClasses(storageClasses.value)
    success.value = true
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to save storage classes.'
  } finally {
    loading.value = false
  }
}
</script>
