<template>
  <!-- Page header -->
  <div class="mb-6">
    <h1 class="text-2xl md:text-3xl text-gray-800 dark:text-gray-100 font-bold">
      Instance Settings
    </h1>
    <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">
      Configure global storage, chunk stores, and instance-wide settings.
    </p>
  </div>

  <!-- Top-level tab bar -->
  <Tabs
    :tabs="tabs"
    default-tab="general"
    :sync-with-route-hash="true"
  />


<!-- Tab content -->
<GeneralSettings
  v-if="activeTab === 'general'"
  :initialSection="generalSubsection"
/>
<ChunkStoresSettings v-else-if="activeTab === 'chunkstores'" />
<StorageClassesSettings v-else-if="activeTab === 'storageclasses'" />
<StorageDefaultMappings v-else-if="activeTab === 'mappings'" />
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import type { TabItem } from '@/shared/components/navigation/Tabs.vue'

import GeneralSettings from '@/features/instance/features/settings/components/GeneralSettings.vue'
import ChunkStoresSettings from '@/features/instance/features/settings/components/ChunkStoresSettings.vue'
import StorageClassesSettings from '@/features/instance/features/settings/components/StorageClassesSettings.vue'
import StorageDefaultMappings from '@/features/instance/features/settings/components/StorageDefaultMappings.vue'
import {
  IconAdjustmentsHorizontal,
  IconDatabase,
  IconStack,
  IconArrowsRightLeft,
} from '@tabler/icons-vue'


const route = useRoute()

const generalAliases = ['email', 'sso', 'tenancy', 'branding', 'domain', 'tasks']
const generalAliasSet = new Set(generalAliases)

const tabs: TabItem[] = [
  {
    id: 'general',
    label: 'General',
    icon: IconAdjustmentsHorizontal,
    hash: 'general',
    aliases: generalAliases,
  },
  {
    id: 'chunkstores',
    label: 'Chunk Stores',
    icon: IconDatabase,
    hash: 'chunkstores',
  },
  {
    id: 'storageclasses',
    label: 'Storage Classes',
    icon: IconStack,
    hash: 'storageclasses',
  },
  {
    id: 'mappings',
    label: 'Default Mappings',
    icon: IconArrowsRightLeft,
    hash: 'mappings',
  },
]

const activeTab = computed(() => {
  const currentHash = route.hash.replace(/^#/, '')

  const matchedTab = tabs.find(tab => {
    const mainHash = tab.hash ?? tab.id
    return mainHash === currentHash || (tab.aliases?.includes(currentHash) ?? false)
  })

  return matchedTab?.id ?? 'general'
})

const generalSubsection = computed<string | null>(() => {
  const currentHash = route.hash.replace(/^#/, '')
  return generalAliasSet.has(currentHash) ? currentHash : null
})
</script>