<template>
  <div class="flex flex-wrap gap-1 mb-6 border-b border-gray-200 dark:border-gray-700">
    <button
      v-for="tab in tabs"
      :key="tab.id"
      type="button"
      class="px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition whitespace-nowrap"
      :class="resolvedActiveTab === tab.id ? activeClass : inactiveClass"
      @click="selectTab(tab)"
    >
      <span class="flex items-center gap-2">
        <component
          v-if="tab.icon"
          :is="tab.icon"
          class="w-4 h-4"
        />
        {{ tab.label }}
      </span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { Component } from 'vue'

export interface TabItem {
  id: string
  label: string
  icon?: Component
  hash?: string
  aliases?: string[]
}

const props = withDefaults(defineProps<{
  tabs: TabItem[]
  modelValue?: string | null
  defaultTab?: string
  syncWithRouteHash?: boolean
  activeClass?: string
  inactiveClass?: string
}>(), {
  modelValue: null,
  defaultTab: '',
  syncWithRouteHash: false,
  activeClass: 'border-violet-500 text-violet-600 dark:text-violet-400',
  inactiveClass: 'border-transparent text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200',
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'change', value: string, tab: TabItem): void
}>()

const route = useRoute()
const router = useRouter()

function normalizeHash(hash: string | null | undefined): string {
  return (hash ?? '').replace(/^#/, '')
}

function findTabByHash(hash: string): TabItem | undefined {
  const normalized = normalizeHash(hash)

  return props.tabs.find(tab => {
    const mainHash = tab.hash ?? tab.id
    return mainHash === normalized || (tab.aliases?.includes(normalized) ?? false)
  })
}

const resolvedActiveTab = computed(() => {
  if (props.modelValue) {
    return props.modelValue
  }

  if (props.syncWithRouteHash) {
    const matched = findTabByHash(route.hash)
    if (matched) return matched.id
  }

  return props.defaultTab || props.tabs[0]?.id || ''
})

async function selectTab(tab: TabItem) {
  emit('update:modelValue', tab.id)
  emit('change', tab.id, tab)

  if (props.syncWithRouteHash) {
    const targetHash = `#${tab.hash ?? tab.id}`

    if (route.hash !== targetHash) {
      await router.push({ hash: targetHash })
    }
  }
}
</script>