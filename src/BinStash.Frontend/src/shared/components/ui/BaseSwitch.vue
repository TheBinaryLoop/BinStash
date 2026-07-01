<template>
  <label class="flex items-center gap-3 select-none" :class="disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer'">
    <button
      type="button"
      role="switch"
      :aria-checked="model"
      :disabled="disabled"
      class="relative inline-flex h-5 w-9 shrink-0 items-center rounded-full p-0.5 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/50 focus-visible:ring-offset-2 focus-visible:ring-offset-canvas"
      :class="model ? 'bg-accent' : 'bg-raised border border-hairline'"
      @click="model = !model"
    >
      <span
        class="h-4 w-4 rounded-full bg-white shadow-sm transition-transform"
        :class="model ? 'translate-x-4' : 'translate-x-0'"
      />
    </button>
    <span v-if="label || $slots.default" class="min-w-0">
      <span class="text-sm font-medium text-ink-strong"><slot>{{ label }}</slot></span>
      <span v-if="description" class="mt-0.5 block text-xs text-ink-muted">{{ description }}</span>
    </span>
  </label>
</template>

<script setup lang="ts">
const model = defineModel<boolean>()

defineProps<{
  label?: string
  description?: string
  disabled?: boolean
}>()
</script>
