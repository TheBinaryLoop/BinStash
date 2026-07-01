<template>
  <div class="flex flex-col items-center justify-center px-6 py-12 text-center">
    <div
      v-if="icon"
      class="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-accent-soft text-accent"
    >
      <component :is="icon" class="h-7 w-7" />
    </div>
    <h3 class="text-base font-semibold text-ink-strong">{{ title }}</h3>
    <p v-if="description" class="mt-1.5 max-w-md text-sm text-ink-muted">{{ description }}</p>
    <div v-if="$slots.default" class="mt-5 flex items-center gap-3">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{
  title: string
  description?: string
  icon?: Component
}>()
</script>
