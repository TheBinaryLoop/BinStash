<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-ink-strong">Step: Configure Default Tenant</h2>
    <p class="text-sm text-ink-muted">
      Set up the name and slug for your default tenant.
    </p>
    <BaseInput
      v-model="name"
      label="Tenant Name"
      type="text"
      required
      :disabled="loading"
    />
    <BaseInput
      v-model="slug"
      label="Tenant Slug"
      type="text"
      required
      :disabled="loading"
    />
    <div
      v-if="success"
      class="rounded-card border border-success/25 bg-success-soft px-4 py-3 text-sm text-success"
    >
      Default tenant configured successfully.
    </div>
    <div
      v-if="error"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >{{ error }}</div>
    <BaseButton
      type="submit"
      :loading="loading"
      :disabled="loading || !name || !slug"
    >
      {{ loading ? 'Saving...' : 'Save Default Tenant' }}
    </BaseButton>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { configureDefaultTenant } from '@/features/setup/api/setup.api'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { BaseInput, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const name = ref('')
const slug = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

async function onSubmit() {
  loading.value = true
  error.value = null
  success.value = false
  try {
    await configureDefaultTenant({
      name: name.value.trim(),
      slug: slug.value.trim(),
    })
    success.value = true
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to configure default tenant.'
  } finally {
    loading.value = false
  }
}
</script>
