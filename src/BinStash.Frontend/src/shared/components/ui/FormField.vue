<template>
  <div class="space-y-1.5">
    <label v-if="label" :for="forId" class="block text-sm font-medium text-ink-strong">
      {{ label }}<span v-if="required" class="ml-0.5 text-danger">*</span>
    </label>
    <slot :id="forId" />
    <p v-if="error" class="text-xs text-danger">{{ error }}</p>
    <p v-else-if="hint" class="text-xs text-ink-subtle">{{ hint }}</p>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  label?: string
  hint?: string
  error?: string
  required?: boolean
  forId?: string
}>()
</script>
