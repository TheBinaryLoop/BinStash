<template>
  <div :class="['px-3 py-2 rounded-lg', toneClasses]" role="alert">
    <span class="text-sm">
      <slot>{{ message }}</slot>
    </span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    tone?: 'info' | 'success' | 'warning' | 'error'
    message?: string
  }>(),
  {
    tone: 'info',
    message: undefined,
  }
)

const toneClasses = computed(() => {
  switch (props.tone) {
    case 'success':
      return 'bg-success-soft text-success'
    case 'warning':
      return 'bg-warning-soft text-warning'
    case 'error':
      return 'bg-danger-soft text-danger'
    case 'info':
    default:
      return 'bg-accent-soft text-accent'
  }
})
</script>