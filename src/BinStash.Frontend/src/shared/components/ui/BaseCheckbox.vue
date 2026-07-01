<template>
  <label class="flex cursor-pointer items-start gap-3 select-none" :class="disabled ? 'cursor-not-allowed opacity-50' : ''">
    <input
      type="checkbox"
      v-model="model"
      :value="value"
      :disabled="disabled"
      class="form-checkbox mt-0.5 h-4 w-4 shrink-0 rounded"
    />
    <span class="min-w-0">
      <span class="text-sm text-ink-strong"><slot>{{ label }}</slot></span>
      <span v-if="description" class="mt-0.5 block text-xs text-ink-muted">{{ description }}</span>
    </span>
  </label>
</template>

<script setup lang="ts">
const model = defineModel<boolean | unknown[]>()

defineProps<{
  label?: string
  description?: string
  value?: unknown
  disabled?: boolean
}>()
</script>
