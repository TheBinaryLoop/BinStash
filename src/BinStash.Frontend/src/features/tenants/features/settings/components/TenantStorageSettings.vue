<template>
  <BaseCard :padded="false">
    <template #header>
      <h2 class="font-semibold text-ink-strong">Storage Classes</h2>
    </template>

    <div class="p-5">
      <div v-if="loadingClasses" class="flex justify-center py-6">
        <Spinner :size="24" color="var(--color-accent)" />
      </div>
      <div v-else-if="storageClasses.length === 0" class="text-sm text-ink-subtle">No storage classes configured.</div>

      <div v-else class="space-y-2">
        <div
          v-for="sc in storageClasses"
          :key="sc.name"
          class="flex items-center justify-between gap-4 rounded-card border border-hairline bg-raised px-4 py-3"
        >
          <div>
            <span class="text-sm font-medium text-ink-strong">{{ sc.name }}</span>
            <span v-if="sc.description" class="ml-2 text-xs text-ink-muted">{{ sc.description }}</span>
          </div>

          <BaseBadge v-if="sc.isDefault" tone="success">Default</BaseBadge>
        </div>
      </div>
    </div>
  </BaseCard>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { listStorageClasses, type StorageClassDto } from '@/api/tenants'
import { BaseCard, BaseBadge } from '@/shared/components/ui'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const loadingClasses = ref(true)
const storageClasses = ref<StorageClassDto[]>([])

onMounted(async () => {
  try {
    storageClasses.value = await listStorageClasses()
  } catch {
    storageClasses.value = []
  } finally {
    loadingClasses.value = false
  }
})
</script>
