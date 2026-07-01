<template>
  <PageHeader title="Tenant Settings" description="View and manage settings for this workspace." />

  <!-- Top-level tab bar -->
  <Tabs
    :tabs="tabs"
    default-tab="general"
    :sync-with-route-hash="true"
  />

  <GeneralSettings v-if="activeTab === 'general'" :initialSection="generalSubsection" />
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { IconAdjustmentsHorizontal } from '@tabler/icons-vue'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import type { TabItem } from '@/shared/components/navigation/Tabs.vue'
import { PageHeader } from '@/shared/components/ui'

import GeneralSettings from '@/features/tenants/features/settings/components/GeneralSettings.vue'

const route = useRoute()

const generalAliases = ['profile', 'storage', 'sso', 'membership', 'integrations']
const generalAliasSet = new Set(generalAliases)

const tabs: TabItem[] = [
  {
    id: 'general',
    label: 'General',
    icon: IconAdjustmentsHorizontal,
    hash: 'general',
    aliases: generalAliases,
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