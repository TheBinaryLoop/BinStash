<template>
  <span
    class="inline-flex max-w-full items-center gap-2 truncate rounded-full border px-3 py-1.5 text-xs font-semibold transition duration-200 lg:h-0 lg:overflow-hidden lg:opacity-0 lg:sidebar-expanded:h-auto lg:sidebar-expanded:opacity-100"
    :class="toneClass"
  >
    <component :is="icon" v-if="icon" class="h-3 w-3 shrink-0" />
    <span class="truncate">{{ label }}</span>
  </span>
</template>

<script setup lang="ts">
import { computed, type Component } from 'vue'

const props = withDefaults(defineProps<{
  label: string
  tone?: 'accent' | 'success'
  icon?: Component
}>(), {
  tone: 'accent',
})

const toneClass = computed(() =>
  props.tone === 'success'
    ? 'border-success/30 bg-success-soft text-success'
    : 'border-accent/30 bg-accent-soft text-accent',
)
</script>
