<template>
  <div class="grid gap-3" :class="columns === 2 ? 'sm:grid-cols-2' : columns === 3 ? 'sm:grid-cols-3' : ''">
    <button
      v-for="opt in options"
      :key="String(opt.value)"
      type="button"
      :disabled="disabled"
      class="flex items-start gap-3 rounded-control border p-3.5 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40"
      :class="model === opt.value
        ? 'border-accent bg-accent-soft'
        : 'border-hairline bg-card hover:border-ink-subtle/50 hover:bg-raised'"
      @click="model = opt.value"
    >
      <component
        :is="opt.icon"
        v-if="opt.icon"
        class="mt-0.5 h-5 w-5 shrink-0"
        :class="model === opt.value ? 'text-accent' : 'text-ink-subtle'"
      />
      <span class="min-w-0">
        <span class="block text-sm font-medium text-ink-strong">{{ opt.label }}</span>
        <span v-if="opt.description" class="mt-0.5 block text-xs text-ink-muted">{{ opt.description }}</span>
      </span>
      <span
        class="ml-auto mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full border transition"
        :class="model === opt.value ? 'border-accent' : 'border-hairline'"
      >
        <span v-if="model === opt.value" class="h-2 w-2 rounded-full bg-accent" />
      </span>
    </button>
  </div>
</template>

<script setup lang="ts">
import type { Component } from 'vue'

export interface RadioOption {
  value: string | number
  label: string
  description?: string
  icon?: Component
}

const model = defineModel<string | number>()

withDefaults(defineProps<{
  options: RadioOption[]
  columns?: 1 | 2 | 3
  disabled?: boolean
}>(), {
  columns: 1,
})
</script>
