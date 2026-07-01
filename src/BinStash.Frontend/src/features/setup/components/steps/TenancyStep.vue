<template>
  <form
    @submit.prevent="onSubmit"
    class="flex flex-col gap-4"
  >
    <h2 class="text-xl font-bold text-ink-strong">Step 2: Select Tenancy Mode</h2>
    <p class="text-sm text-ink-muted">
      Choose whether this BinStash instance will operate in Single-tenant or Multi-tenant mode.
    </p>
    <div
      v-if="locked"
      class="rounded-card border border-warning/25 bg-warning-soft px-4 py-3 text-sm text-warning"
    >
      <strong>Configured in appsettings/env and cannot be changed here.</strong>
    </div>
    <BaseRadioGroup
      v-else
      v-model="mode"
      :columns="2"
      :options="modeOptions"
      :disabled="loading"
    />
    <div
      v-if="error"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >{{ error }}</div>
    <BaseButton
      type="submit"
      :loading="loading"
      :disabled="loading || !mode || locked"
    >
      {{ loading ? 'Saving...' : locked ? 'Locked' : 'Save Tenancy Mode' }}
    </BaseButton>
  </form>
</template>

<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { setTenancyMode, getSetupStatus } from '@/features/setup/api/setup.api'
import { BaseRadioGroup, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const status = computed(() => setupStore.status)
const loading = ref(false)
const error = ref<string | null>(null)
const mode = ref<'Single' | 'Multi' | null>(null)
const locked = ref(false)

const modeOptions = [
  { value: 'Single', label: 'Single', description: 'One company workspace for this instance.' },
  { value: 'Multi', label: 'Multi', description: 'Multiple isolated tenants on this instance.' },
]

onMounted(() => {
  if (status.value?.data?.tenancyMode) {
    mode.value = status.value.data.tenancyMode
  }
  if (status.value && typeof status.value.data?.tenancyMode === 'string') {
    // If backend returns locked, we should fetch it after submit, but for now assume not locked
    locked.value = false
  }
})

async function onSubmit() {
  if (!mode.value) return
  loading.value = true
  error.value = null
  try {
    const res = await setTenancyMode(mode.value)
    locked.value = !!res.locked
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to set tenancy mode.'
  } finally {
    loading.value = false
  }
}
</script>
