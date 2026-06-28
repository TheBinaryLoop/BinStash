<template>
  <div class="flex-1 flex flex-col">
    <!-- Progress bar -->
    <div class="px-4 pt-12 pb-8">
      <div class="max-w-md mx-auto w-full">
        <div class="relative">
          <div
            class="absolute left-0 top-1/2 -mt-px w-full h-0.5 bg-gray-200 dark:bg-gray-700/60"
            aria-hidden="true"
          ></div>
          <ol class="relative flex justify-between w-full">
            <li v-for="(step, i) in steps" :key="step.key">
              <div
                class="flex items-center justify-center w-6 h-6 rounded-full text-xs font-semibold"
                :class="
                  i === stepIndex
                    ? 'bg-violet-500 text-white'
                    : 'bg-white dark:bg-gray-900 text-gray-500 dark:text-gray-400 border border-gray-200 dark:border-gray-700/60'
                "
              >
                {{ i + 1 }}
              </div>
            </li>
          </ol>
        </div>
      </div>
    </div>

    <!-- Card -->
    <div class="px-4 py-8 flex-1 flex flex-col">
      <div
        class="max-w-none mx-auto w-full bg-white dark:bg-gray-800 rounded-lg shadow-lg p-8 flex flex-col gap-4"
      >
        <h1 class="text-2xl font-bold text-gray-800 dark:text-gray-100 mb-2 text-center">
          BinStash Setup Wizard
        </h1>

        <div v-if="loading" class="setup-loading text-center">Loading...</div>

        <div v-else>
          <div v-if="error" class="setup-error text-center mb-2">{{ error }}</div>
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