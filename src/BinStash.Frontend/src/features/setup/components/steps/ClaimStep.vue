<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-ink-strong">Step 1: Enter Setup Code</h2>
    <p class="text-sm text-ink-muted">
      Enter the setup code from the server logs/console to begin the BinStash setup process.
    </p>
    <BaseInput
      v-model="code"
      label="Setup Code"
      type="text"
      autocomplete="off"
      required
      placeholder="ABCD-EFGH-IJKL-MNOP"
      :disabled="loading"
    />
    <div
      v-if="error"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >{{ error }}</div>
    <BaseButton type="submit" :loading="loading" :disabled="loading">
      {{ loading ? 'Verifying...' : 'Start Setup Session' }}
    </BaseButton>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { claimSetupSession } from '@/features/setup/api/setup.api'
import { BaseInput, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const code = ref('')
const loading = ref(false)
const error = ref<string | null>(null)

async function onSubmit() {
  loading.value = true
  error.value = null
  try {
    await claimSetupSession(code.value.trim())
    // Wait briefly to allow the setup cookie to be set before fetching status
    await new Promise(resolve => setTimeout(resolve, 200))
    await setupStore.fetchStatus()
    code.value = ''
  } catch (e: any) {
    if (e.message === 'Request failed (401)') {
      error.value = 'Invalid or expired setup code.'
    } else if (e.message === 'setup_required') {
      error.value = 'Setup is required but could not be claimed. Try again.'
    } else {
      error.value = e.message || 'Failed to claim setup session.'
    }
  } finally {
    loading.value = false
  }
}
</script>
