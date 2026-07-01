<template>
  <div class="flex-1 flex flex-col">
    <!-- Progress bar -->
    <div class="px-4 pt-12 pb-8">
      <div class="mx-auto w-full max-w-3xl">
        <Stepper :steps="stepLabels" :current="stepIndex" :show-labels="false" />
      </div>
    </div>

    <!-- Card -->
    <div class="px-4 py-8 flex-1 flex flex-col">
      <div class="mx-auto w-full max-w-none rounded-card border border-hairline bg-card p-8 shadow-sm flex flex-col gap-4">
        <h1 class="mb-2 text-center text-2xl font-bold text-ink-strong">
          BinStash Setup Wizard
        </h1>

        <div v-if="loading" class="flex items-center justify-center py-8">
          <Spinner :size="24" color="var(--color-accent)" />
        </div>

        <div v-else>
          <div
            v-if="error"
            class="mb-4 rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-center text-sm text-danger"
          >{{ error }}</div>
          <component :is="currentStepComponent" />
        </div>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { Stepper } from '@/shared/components/ui'
import Spinner from '@/shared/components/feedback/Spinner.vue'

import ClaimStep from '@/features/setup/components/steps/ClaimStep.vue'
import TenancyStep from '@/features/setup/components/steps/TenancyStep.vue'
import DefaultTenantStep from '@/features/setup/components/steps/DefaultTenantStep.vue'
import ChunkStoreStep from '@/features/setup/components/steps/ChunkStoreStep.vue'
import StorageClassesStep from '@/features/setup/components/steps/StorageClassesStep.vue'
import StorageMappingsStep from '@/features/setup/components/steps/StorageDefaultsStep.vue'
import AdminStep from '@/features/setup/components/steps/AdminStep.vue'
import ReviewStep from '@/features/setup/components/steps/ReviewStep.vue'
import FinishStep from '@/features/setup/components/steps/FinishStep.vue'

const router = useRouter()
const setupStore = useSetupStore()

const steps = [
  { key: 'claim', title: 'Claim Setup', component: ClaimStep },
  { key: 'tenancy', title: 'Tenancy Mode', component: TenancyStep },
  { key: 'defaulttenant', title: 'Default Tenant', component: DefaultTenantStep },
  { key: 'chunkstore', title: 'Chunk Stores', component: ChunkStoreStep },
  { key: 'storageclass', title: 'Storage Classes', component: StorageClassesStep },
  { key: 'storageclassdefaultmappings', title: 'Storage Class Default Mappings', component: StorageMappingsStep },
  { key: 'instanceadmin', title: 'Instance Admin User', component: AdminStep },
  { key: 'tenantadmin', title: 'Tenant Admin User', component: AdminStep },
  { key: 'review', title: 'Review', component: ReviewStep },
  { key: 'done', title: 'Finish', component: FinishStep },
]

const stepLabels = steps.map(s => s.title)

function getStepIndex(status: any): number {
  if (!status) return 0
  if (!status.currentStep) return 0

  const step =
    typeof status.currentStep === 'string'
      ? status.currentStep.toLowerCase()
      : status.currentStep

  switch (step) {
    case 'claim': return 0
    case 'tenancy': return 1
    case 'defaulttenant': return 2
    case 'chunkstore': return 3
    case 'storageclass': return 4
    case 'storageclassdefaultmappings': return 5
    case 'instanceadmin': return 6
    case 'tenantadmin': return 7
    case 'review': return 8
    case 'done': return 9
    default: return 0
  }
}

const loading = computed(() => setupStore.loading)
const error = computed(() => setupStore.error)
const status = computed(() => setupStore.status)
const isInitialized = computed(() => setupStore.isInitialized)
const stepIndex = computed(() => getStepIndex(status.value))

const currentStepComponent = computed(() => {
  if (error.value === 'unauthenticated') return ClaimStep
  return steps[stepIndex.value]?.component ?? ClaimStep
})

onMounted(async () => {
  await setupStore.fetchStatus()
  if (isInitialized.value) {
    router.replace('/')
  }
})
</script>
