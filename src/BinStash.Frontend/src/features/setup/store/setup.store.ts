import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { getSetupStatus } from '@/features/setup/api/setup.api'

export const useSetupStore = defineStore('setup', () => {
  // State
  const status = ref<null | {
    isInitialized: boolean
    currentStep: string | null
    setupVersion?: string
    data: {
      tenancyMode: 'Single' | 'Multi' | null
      chunkStores: { id: string; name: string; enabled: boolean }[]
      storageClasses: { name: string; displayName: string; description?: string }[]
      storageClassDefaultMappings: { storageClassName: string; chunkStoreId: string; isDefault: boolean; isEnabled: boolean }[]
      tenants: { tenantId: string; name: string; slug: string }[]
      instanceAdmins: { id: string; email: string; firstName?: string; lastName?: string }[]
      tenantAdmins: { id: string; email: string; firstName?: string; lastName?: string }[]
    }
  }>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Derived
  const isInitialized = computed(() => status.value?.isInitialized ?? false)
  const tenancyMode = computed(() => status.value?.data?.tenancyMode ?? null)
  const currentStep = computed(() => status.value?.currentStep ?? null)

  // Actions
  async function fetchStatus() {
    loading.value = true
    error.value = null
    try {
      const result = await getSetupStatus()
      status.value = result
    } catch (e: any) {
      error.value = e?.message || 'Failed to fetch setup status'
    } finally {
      loading.value = false
    }
  }

  return {
    status,
    loading,
    error,
    isInitialized,
    tenancyMode,
    currentStep,
    fetchStatus,
  }
})
