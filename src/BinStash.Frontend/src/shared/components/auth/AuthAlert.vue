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
      return 'bg-emerald-500/20 text-emerald-700 dark:text-emerald-200'
    case 'warning':
      return 'bg-amber-500/20 text-amber-700 dark:text-amber-200'
    case 'error':
      return 'bg-rose-500/20 text-rose-700 dark:text-rose-200'
    case 'info':
    default:
      return 'bg-violet-500/15 text-violet-700 dark:text-violet-200'
  }
})
</script>