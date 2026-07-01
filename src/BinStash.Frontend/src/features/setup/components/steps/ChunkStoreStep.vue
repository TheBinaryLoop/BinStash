<template>
  <div class="flex flex-col gap-4">
    <template v-if="existingChunkstores.length > 0 && !showForm">
      <h2 class="text-xl font-bold text-ink-strong">Step 4: Configure Local Chunkstore</h2>
      <p class="text-sm text-ink-muted">
        The following chunkstores are already configured:
      </p>
      <ul class="ml-5 list-disc text-sm text-ink-muted">
        <li v-for="store in existingChunkstores" :key="store.id">
          <strong class="text-ink-strong">{{ store.name }}</strong>
          <span class="text-ink-subtle">({{ store.id }})</span>
        </li>
      </ul>
      <div class="mt-4 flex gap-3">
        <BaseButton variant="secondary" @click="handleAddAnother">
          Add another chunkstore
        </BaseButton>
        <BaseButton @click="handleContinue">
          Continue
        </BaseButton>
      </div>
    </template>
    <form v-else @submit.prevent="onSubmit" class="flex flex-col gap-4">
      <h2 class="text-xl font-bold text-ink-strong">Step 3: Configure Local Chunkstore</h2>
      <p class="text-sm text-ink-muted">
        Configure the local chunkstore for BinStash. You can use the default name and path or customize them.
      </p>
      <BaseInput
        v-model="name"
        label="Chunkstore Name"
        type="text"
        required
        :disabled="loading"
        autocomplete="off"
      />
      <BaseSelect v-model="type" label="Chunkstore Type">
        <option :value="-1" disabled>- Select a type -</option>
        <option v-for="chunkstoreType in enabledChunkStoreTypes" :key="chunkstoreType.value" :value="chunkstoreType.value">{{ chunkstoreType.name }}</option>
      </BaseSelect>
      <BaseInput
        v-model="localPath"
        label="Local Path"
        type="text"
        required
        :disabled="loading"
        autocomplete="off"
      />
      <div
        v-if="chunkStoreId"
        class="rounded-card border border-success/25 bg-success-soft px-4 py-3 text-sm text-success"
      >
        Chunkstore created! ID: <code class="rounded bg-raised px-1 py-0.5 font-mono text-xs">{{ chunkStoreId }}</code>
      </div>
      <div
        v-if="error"
        class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
      >{{ error }}</div>
      <BaseButton type="submit" :loading="loading" :disabled="loading">
        {{ loading ? 'Creating...' : 'Create Chunkstore' }}
      </BaseButton>
    </form>
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { listChunkStores, createChunkStore as createApiChunkStore, getEnabledChunkStoreTypes, setChunkStoreDone } from '@/features/setup/api/setup.api'
import { ChunkStoreSummaryDto } from '@/api/chunkStores'
import { BaseInput, BaseSelect, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const name = ref('local')
const localPath = ref('')
const type = ref(-1)
const enabledChunkStoreTypes = ref<{ name: string, value: number }[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const chunkStoreId = ref<string | null>(null)
const existingChunkstores = ref<ChunkStoreSummaryDto[]>([])
const showForm = ref(false)

function getDefaultPath() {
  const ua = navigator.userAgent
  if (/Windows/i.test(ua)) return 'C:\\BinStash\\Chunks'
  if (/Linux/i.test(ua)) return '/var/lib/binstash/chunks'
  return './data/chunks'
}

async function fetchChunkstores() {
  try {
    existingChunkstores.value = await listChunkStores()
  } catch {
    existingChunkstores.value = []
  }
}

onMounted(async () => {
  localPath.value = getDefaultPath()
  getEnabledChunkStoreTypes()
    .then(types => {
      enabledChunkStoreTypes.value = types
    })
    .catch(() => {
      error.value = 'Failed to fetch chunkstore types.'
    })
  await fetchChunkstores()
  showForm.value = existingChunkstores.value.length === 0
})

async function onSubmit() {
  loading.value = true
  error.value = null
  chunkStoreId.value = null
  try {
    const res = await createApiChunkStore({
      type: type.value.toString(),
      name: name.value.trim(),
      localPath: localPath.value.trim(),
    })
    chunkStoreId.value = res.id
    await fetchChunkstores()
    showForm.value = false
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to create chunkstore.'
  } finally {
    loading.value = false
  }
}

function handleAddAnother() {
  name.value = 'local'
  localPath.value = getDefaultPath()
  type.value = -1
  chunkStoreId.value = null
  error.value = null
  showForm.value = true
}

async function handleContinue() {
  showForm.value = false
  await markChunkStoreDone()
}

async function markChunkStoreDone() {
  loading.value = true
  error.value = null
  try {
    await setChunkStoreDone()
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to mark chunkstore as done.'
  } finally {
    loading.value = false
  }
}
</script>
